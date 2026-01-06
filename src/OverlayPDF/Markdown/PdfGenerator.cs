using iText.Html2pdf;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout.Font;
using Markdig;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace OverlayPDF.Markdown;

/// <summary>
/// Generates PDF documents from markdown content.
/// </summary>
public class PdfGenerator(IOptions<PdfOverlayOptions> options, MarkdownProcessor markdownProcessor)
{
    private readonly PdfOverlayOptions _overlayOptions = options.Value;

    /// <summary>
    /// Generates a PDF from markdown content.
    /// </summary>
    public void GeneratePdfFromMarkdown(string markdownPath, string outputPdfPath, PageSize pageSize)
    {
        // Read markdown
        var markdown = File.ReadAllText(markdownPath);

        // Process markdown placeholders and special blocks
        markdown = markdownProcessor.ProcessMarkdown(markdown);

        // Convert markdown to HTML using Markdig
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var html = Markdig.Markdown.ToHtml(markdown, pipeline);

        // Apply CSS styling
        var fontFamily = _overlayOptions.DefaultFontFamily;
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
            catch
            {
                fontsDir = null;
            }
        }

        if (!string.IsNullOrEmpty(fontsDir) && Directory.Exists(fontsDir))
        {
            // Register all fonts in the directory
            fontProvider.AddDirectory(fontsDir);

            try
            {
                var poppinsLightFiles = Directory.GetFiles(fontsDir, "*.ttf", SearchOption.TopDirectoryOnly);
                foreach (var fontFile in poppinsLightFiles)
                {
                    fontProvider.AddFont(fontFile);
                }
            }
            catch
            {
                // ignore font discovery errors
            }
        }

        converterProperties.SetFontProvider(fontProvider);

        HtmlConverter.ConvertToPdf(html, pdf, converterProperties);
    }
}
