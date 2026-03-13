using iText.Html2pdf;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Navigation;
using iText.Layout.Font;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace OverlayPDF.Markdown;

/// <summary>
/// Generates PDF documents from markdown content.
/// </summary>
public class PdfGenerator(IOptions<PdfOverlayOptions> options, MarkdownProcessor markdownProcessor, ILogger<PdfGenerator> logger)
{
    private readonly PdfOverlayOptions _overlayOptions = options.Value;

    /// <summary>
    /// Generates a PDF from markdown content with template overlays applied during generation.
    /// This preserves AcroForm fields by copying pages directly instead of using CopyAsFormXObject.
    /// </summary>
    public void GeneratePdfFromMarkdownWithTemplates(string markdownPath, string outputPdfPath,
        string firstTemplatePath, string continuationTemplatePath)
    {
        // Get page size from template
        using var initialTemplateDoc = new PdfDocument(new PdfReader(firstTemplatePath));
        var templateRect = initialTemplateDoc.GetPage(1).GetPageSize();
        var pageSize = new PageSize(templateRect.GetWidth(), templateRect.GetHeight());

        // Extract headings once here so we don't need to re-read the file inside the try block.
        var rawMarkdown = File.ReadAllText(markdownPath);
        var headings = ExtractHeadings(rawMarkdown);

        // First, generate PDF from markdown to a temporary location
        var tempContentPdf = Path.ChangeExtension(Path.GetTempFileName(), ".pdf");
        try
        {
            // Generate PDF with margins already applied in the content
            GeneratePdfFromMarkdown(markdownPath, tempContentPdf, pageSize);

            // Now merge with templates, preserving form fields
            using var contentPdfDoc = new PdfDocument(new PdfReader(tempContentPdf));
            using var continuationTemplateDoc = new PdfDocument(new PdfReader(continuationTemplatePath));

            var totalContentPages = contentPdfDoc.GetNumberOfPages();
            if (totalContentPages < 1)
            {
                logger.LogError("The generated content PDF has no pages.");
                return;
            }

            // Create the output PDF
            using var writer = new PdfWriter(outputPdfPath);
            using var outputPdfDoc = new PdfDocument(writer);

            // Pre-copy template XObjects (templates don't have form fields, so this is safe)
            var firstTemplateXObject = initialTemplateDoc.GetPage(1).CopyAsFormXObject(outputPdfDoc);
            var contTemplateXObject = continuationTemplateDoc.GetPage(1).CopyAsFormXObject(outputPdfDoc);

            // Process each page
            for (var i = 1; i <= totalContentPages; i++)
            {
                var useFirstTemplate = i == 1;
                var templateXObject = useFirstTemplate ? firstTemplateXObject : contTemplateXObject;
                var appliedTemplatePath = useFirstTemplate ? firstTemplatePath : continuationTemplatePath;

                // Copy the page from content PDF (preserves form fields)
                contentPdfDoc.CopyPagesTo(i, i, outputPdfDoc);
                var importedPage = outputPdfDoc.GetPage(outputPdfDoc.GetNumberOfPages());

                // Add template as background (before content)
                var canvas = new PdfCanvas(importedPage.NewContentStreamBefore(), importedPage.GetResources(), outputPdfDoc);
                canvas.AddXObjectAt(templateXObject, 0, 0);

                logger.LogInformation("Applied template to page {Page}: {Template}", i, appliedTemplatePath);
            }

            // Copy named destinations (e.g. heading anchors for TOC links) from the content PDF
            // to the output PDF. CopyPagesTo copies page-level link annotations but does not
            // propagate the document-level name tree, so GoTo actions would silently fail without this.
            var srcNames = contentPdfDoc.GetCatalog().GetNameTree(PdfName.Dests).GetNames();
            if (srcNames?.Count > 0)
            {
                var dstNameTree = outputPdfDoc.GetCatalog().GetNameTree(PdfName.Dests);
                foreach (var (key, value) in srcNames)
                    dstNameTree.AddEntry(key, value.CopyTo(outputPdfDoc));

                logger.LogDebug("Copied {Count} named destination(s) to output PDF", srcNames.Count);
            }

            // Add PDF bookmarks (outline) to the output PDF so that the TOC sidebar is
            // populated and heading entries are navigable in PDF viewers.
            AddOutlines(outputPdfDoc, headings);
            logger.LogDebug("Added {Count} outline entry/entries to output PDF", headings.Count);
        }
        finally
        {
            // Cleanup temp file
            if (File.Exists(tempContentPdf))
            {
                try
                {
                    File.Delete(tempContentPdf);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete temporary PDF file: {TempFile}", tempContentPdf);
                }
            }
        }
    }

    /// <summary>
    /// Generates a PDF from markdown content.
    /// </summary>
    internal void GeneratePdfFromMarkdown(string markdownPath, string outputPdfPath, PageSize pageSize)
    {
        // Read markdown
        var markdown = File.ReadAllText(markdownPath);

        // Check if markdown contains signature blocks (AcroForm fields needed)
        var hasSignatureBlocks = markdown.Contains("```signatures", StringComparison.OrdinalIgnoreCase);

        // Process markdown placeholders and special blocks
        markdown = markdownProcessor.ProcessMarkdown(markdown);

        // Convert markdown to HTML using Markdig
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var html = Markdig.Markdown.ToHtml(markdown, pipeline);

        // Apply CSS styling with configurable margins
        var fontFamily = _overlayOptions.DefaultFontFamily;

        // Convert points to pixels for CSS (1pt = 1.333px approximately, but we'll use pt directly in CSS)
        var topMarginFirstPage = _overlayOptions.FirstPageTopMarginPoints;
        var bottomMarginFirstPage = _overlayOptions.FirstPageBottomMarginPoints;
        var topMarginContinuation = _overlayOptions.ContinuationTopMarginPoints;
        var bottomMarginContinuation = _overlayOptions.ContinuationBottomMarginPoints;
        var leftMargin = _overlayOptions.LeftMarginPoints;
        var rightMargin = _overlayOptions.RightMarginPoints;

        var style = $$"""
                      <style>
                          body {
                              font-family: {{fontFamily}};
                              font-size: 11pt;
                              line-height: 1.45;
                              color: #222;
                              margin: 0;
                          }

                          h1 {
                              font-size: 22pt;
                              font-weight: bold;
                              margin-top: 0;
                              margin-bottom: 12px;
                              color: #333;
                          }

                          h2 {
                              font-size: 16pt;
                              font-weight: bold;
                              margin-top: 24px;
                              margin-bottom: 10px;
                              color: #444;
                          }

                          h3 {
                              font-size: 13pt;
                              font-weight: bold;
                              margin-top: 18px;
                              margin-bottom: 6px;
                              color: #555;
                          }

                          p {
                              margin: 8px 0 14px 0;
                          }

                          /* iText-safe table styling */
                          table {
                              width: 100%;
                              border-collapse: collapse;
                              margin-bottom: 20px;
                              font-size: 10.5pt;
                          }

                          th {
                              background-color: #efefef;
                              font-weight: bold;
                              padding: 8px;
                              border-bottom: 2px solid #cccccc;
                              text-align: left;
                          }

                          td {
                              padding: 8px;
                              border-bottom: 1px solid #dddddd;
                              vertical-align: top;
                          }

                          /* Zebra striping � iText supports this only in this exact form */
                          tr.even td {
                              background-color: #f7f7f7;
                          }

                          /* Totals row � iText friendly */
                          .total-row td {
                              font-weight: bold;
                              background-color: #f2f2f2;
                              border-top: 2px solid #cccccc;
                              border-bottom: 2px solid #cccccc;
                          }

                          .footer-note {
                              font-size: 9pt;
                              color: #666;
                              margin-top: 20px;
                          }

                          /* Code blocks */
                          pre {
                              font-family: Courier, monospace;
                              font-size: 9pt;
                              background-color: #f5f5f5;
                              border: 1px solid #e0e0e0;
                              padding: 8px 10px;
                              margin: 8px 0;
                              white-space: pre-wrap;
                              word-wrap: break-word;
                          }

                          code {
                              font-family: Courier, monospace;
                              font-size: 9pt;
                              background-color: #f5f5f5;
                              padding: 1px 4px;
                          }

                          pre code {
                              background-color: transparent;
                              padding: 0;
                              border: none;
                          }

                          /* Apply template margins via page rules */
                          @page {
                              margin-top: {{topMarginContinuation}}pt;
                              margin-bottom: {{bottomMarginContinuation}}pt;
                              margin-left: {{leftMargin}}pt;
                              margin-right: {{rightMargin}}pt;
                          }

                          @page :first {
                              margin-top: {{topMarginFirstPage}}pt;
                              margin-bottom: {{bottomMarginFirstPage}}pt;
                              margin-left: {{leftMargin}}pt;
                              margin-right: {{rightMargin}}pt;
                          }
                      </style>
                      """;

        html = style + html;

        // Convert HTML to PDF using iText pdfHTML
        using var writer = new PdfWriter(outputPdfPath);
        using var pdf = new PdfDocument(writer);
        pdf.SetDefaultPageSize(pageSize);

        var converterProperties = new ConverterProperties();
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(markdownPath)) ?? Directory.GetCurrentDirectory();
        converterProperties.SetBaseUri(baseDir);

        // Only enable AcroForm creation if signature blocks are present
        if (hasSignatureBlocks)
        {
            converterProperties.SetCreateAcroForm(true);
            logger.LogDebug("AcroForm creation enabled due to signature blocks in markdown");
        }

        // Register fonts
        var fontProvider = new FontProvider();
        fontProvider.AddStandardPdfFonts();

        var fontsDir = _overlayOptions.FontsDirectory;
        if (string.IsNullOrEmpty(fontsDir))
        {
            try
            {
                fontsDir = Path.Combine(_overlayOptions.TemplateDirectory, "fonts");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to construct fonts directory path from template directory");
                fontsDir = null;
            }
        }

        if (!string.IsNullOrEmpty(fontsDir) && Directory.Exists(fontsDir))
        {
            try
            {
                // Register all fonts in the directory
                fontProvider.AddDirectory(fontsDir);
                logger.LogDebug("Registered fonts from directory: {FontsDir}", fontsDir);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to register fonts from directory: {FontsDir}", fontsDir);
            }
        }

        converterProperties.SetFontProvider(fontProvider);

        // Add PDF bookmarks (outline) before conversion so the catalog entry exists
        // in the document. The outline items use PdfStringDestination (named destinations)
        // which are created by HtmlConverter from heading id attributes; PDF viewers
        // resolve these names when the document is opened.
        var headings = ExtractHeadings(markdown);
        AddOutlines(pdf, headings);

        HtmlConverter.ConvertToPdf(html, pdf, converterProperties);
    }

    /// <summary>
    /// Parses the markdown source and returns all ATX headings as a flat list ordered by
    /// document position, together with the level (1-6), the plain-text title, and the
    /// anchor ID that Markdig's AutoIdentifiers extension assigns to each heading.
    /// </summary>
    internal static List<(int Level, string Text, string Id)> ExtractHeadings(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        var headings = new List<(int Level, string Text, string Id)>();

        foreach (var block in document)
        {
            if (block is not HeadingBlock heading)
                continue;

            var id = heading.TryGetAttributes()?.Id ?? string.Empty;
            if (string.IsNullOrEmpty(id))
                continue;

            var text = GetInlineText(heading.Inline);
            if (!string.IsNullOrWhiteSpace(text))
                headings.Add((heading.Level, text, id));
        }

        return headings;
    }

    /// <summary>
    /// Recursively extracts plain text from a Markdig inline tree.
    /// </summary>
    private static string GetInlineText(ContainerInline? container)
    {
        if (container is null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline nested:
                    sb.Append(GetInlineText(nested));
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a hierarchical PDF outline (bookmark tree) in <paramref name="pdfDoc"/> from the
    /// supplied heading list.  Each entry targets the named destination that iText pdfHTML creates
    /// from the heading's HTML <c>id</c> attribute, so the bookmarks navigate to the correct page.
    /// </summary>
    internal static void AddOutlines(PdfDocument pdfDoc, IReadOnlyList<(int Level, string Text, string Id)> headings)
    {
        if (headings.Count == 0)
            return;

        var root = pdfDoc.GetOutlines(false);

        // Maps heading level → the most-recently-added outline at that level so we can nest children.
        var parentsByLevel = new Dictionary<int, PdfOutline> { [0] = root };

        foreach (var (level, text, id) in headings)
        {
            // Walk up to find the nearest defined parent level.
            var parentLevel = level - 1;
            while (parentLevel > 0 && !parentsByLevel.ContainsKey(parentLevel))
                parentLevel--;

            var parent = parentsByLevel.GetValueOrDefault(parentLevel, root);
            var outline = parent.AddOutline(text);
            outline.AddDestination(new PdfStringDestination(id));

            // Register this entry as the current parent for deeper levels and
            // remove any stale entries from previously-visited deeper levels.
            parentsByLevel[level] = outline;
            foreach (var stale in parentsByLevel.Keys.Where(k => k > level).ToList())
                parentsByLevel.Remove(stale);
        }
    }
}
