using iText.Html2pdf;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout.Font;
using Markdig;
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

        var style = $$"""
                      <style>
                          body {
                              font-family: {{fontFamily}};
                              font-size: 11pt;
                              line-height: 1.45;
                              color: #222;
                              margin: 30px;
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

                          /* Zebra striping – iText supports this only in this exact form */
                          tr.even td {
                              background-color: #f7f7f7;
                          }

                          /* Totals row – iText friendly */
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

                          /* Apply template margins via page rules */
                          @page {
                              margin-top: {{topMarginContinuation}}pt;
                              margin-bottom: {{bottomMarginContinuation}}pt;
                          }

                          @page :first {
                              margin-top: {{topMarginFirstPage}}pt;
                              margin-bottom: {{bottomMarginFirstPage}}pt;
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

        HtmlConverter.ConvertToPdf(html, pdf, converterProperties);
    }
}
