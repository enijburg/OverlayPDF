using System.Collections.Generic;
using System.Linq;
using System.Text;
using iText.Html2pdf;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Navigation;
using iText.Layout.Font;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
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

        // Convert markdown to HTML using Markdig.
        // UseAutoIdentifiers with GitHub mode must come BEFORE UseAdvancedExtensions so that
        // heading anchor IDs are generated by the GitHub algorithm (numbers and hyphens kept,
        // e.g. "# 1. Overview" → id="1-overview").  UseAdvancedExtensions also calls
        // UseAutoIdentifiers internally with Default mode, which would strip the leading number
        // ("# 1. Overview" → id="overview").  Registering GitHub mode first wins because
        // Markdig skips duplicate extension registrations.
        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseAdvancedExtensions()
            .Build();
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

        // iText pdfHTML only creates named destinations (required for bookmarks and
        // internal TOC links) for headings that are referenced by at least one
        // <a href="#id"> link somewhere in the document.  Headings that have an id
        // attribute but are not linked would otherwise produce no named destination,
        // leaving all bookmark entries pointing to page 0.
        //
        // Injecting a hidden <div> with an empty anchor for every heading forces iText
        // to create the destinations without adding any visible content or link
        // annotations to the PDF (display:none suppresses layout).
        var headings = ExtractHeadings(markdown);
        if (headings.Count > 0)
        {
            var sb = new StringBuilder("<div style=\"display:none\">");
            foreach (var (_, _, id) in headings)
                sb.Append($"<a href=\"#{id}\"></a>");
            sb.Append("</div>");
            html += sb.ToString();
        }

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

        HtmlConverter.ConvertToPdf(html, pdf, converterProperties);
        // pdf is now closed by HtmlConverter — named destinations exist in the file.

        // Post-process: reopen the generated PDF and add bookmarks with explicit
        // (page-based) destinations resolved from the populated names tree.
        // Doing this after HtmlConverter ensures the names tree is fully populated,
        // which is required to obtain correct page references for each heading.
        if (headings.Count > 0)
            AddOutlinesToExistingPdf(outputPdfPath, headings);
    }

    /// <summary>
    /// Reopens a finished PDF, adds bookmark (outline) entries whose destinations are
    /// resolved from the document's names tree, and writes back to the same path.
    /// </summary>
    private static void AddOutlinesToExistingPdf(string pdfPath,
        IReadOnlyList<(int Level, string Text, string Id)> headings)
    {
        var tmpPath = pdfPath + ".outlines_tmp";
        File.Move(pdfPath, tmpPath);
        try
        {
            using var reader = new PdfReader(tmpPath);
            using var writer = new PdfWriter(pdfPath);
            using var doc = new PdfDocument(reader, writer);
            AddOutlines(doc, headings);
        }
        catch
        {
            // Restore the original file if the post-processing step failed.
            if (!File.Exists(pdfPath))
                File.Move(tmpPath, pdfPath);
            throw;
        }
        finally
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
    }

    /// <summary>
    /// Parses the markdown source and returns all ATX headings as a flat list ordered by
    /// document position, together with the level (1-6), the plain-text title, and the
    /// anchor ID that Markdig's AutoIdentifiers extension assigns to each heading.
    /// </summary>
    internal static List<(int Level, string Text, string Id)> ExtractHeadings(string markdown)
    {
        // Use the same GitHub-mode pipeline as the HTML conversion to guarantee that the
        // heading IDs extracted here exactly match the id= attributes written into the HTML.
        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseAdvancedExtensions()
            .Build();
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

        var sb = new StringBuilder();
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

        // Build a lookup from heading-id → explicit destination (a PdfArray such as [pageRef /Fit])
        // from the document's names tree.  Using explicit destinations guarantees correct page
        // references; PdfStringDestination (a lazy name lookup) leaves all entries at page 0
        // when the outline is written before HtmlConverter populates the names tree.
        var rawNamesTree = pdfDoc.GetCatalog().GetNameTree(PdfName.Dests).GetNames();
        var namesLookup = rawNamesTree != null
            ? new Dictionary<string, PdfObject>(rawNamesTree.Count, StringComparer.Ordinal)
            : null;
        if (rawNamesTree != null)
            foreach (var kvp in rawNamesTree)
                namesLookup![kvp.Key.GetValue()] = kvp.Value;

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

            // Prefer an explicit destination resolved from the names tree.  This directly
            // references the page object, so PDF viewers can navigate without resolving a
            // string key.  Fall back to a string destination only when the entry is missing.
            if (namesLookup != null && namesLookup.TryGetValue(id, out var destObj))
                outline.AddDestination(PdfDestination.MakeDestination(destObj));
            else
                outline.AddDestination(new PdfStringDestination(id));

            // Register this entry as the current parent for deeper levels and
            // remove any stale entries from previously-visited deeper levels.
            parentsByLevel[level] = outline;
            foreach (var stale in parentsByLevel.Keys.Where(k => k > level).ToList())
                parentsByLevel.Remove(stale);
        }
    }
}
