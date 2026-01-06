using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OverlayPDF;

/// <summary>
/// Merges PDF content with template overlays, handling margins and clipping.
/// </summary>
public class PdfMerger(ILogger<PdfMerger> logger, IOptions<PdfOverlayOptions> options)
{
    private readonly PdfOverlayOptions _overlayOptions = options.Value;

    /// <summary>
    /// Merges a content PDF with template PDFs, applying the first template to page 1
    /// and continuation template to subsequent pages.
    /// </summary>
    public void MergePdfsWithTwoTemplates(string textPdfPath, string outputPdfPath,
        string firstTemplatePath, string continuationTemplatePath)
    {
        // Open the template PDFs and the text PDF
        using var initialTemplateDoc = new PdfDocument(new PdfReader(firstTemplatePath));
        using var continuationTemplateDoc = new PdfDocument(new PdfReader(continuationTemplatePath));
        using var textPdfDoc = new PdfDocument(new PdfReader(textPdfPath));

        // Check that the text PDF has at least one page
        var totalTextPages = textPdfDoc.GetNumberOfPages();
        if (totalTextPages < 1)
        {
            logger.LogError("The text PDF must have at least one page.");
            return;
        }

        // Get the page size from the first page of the initial template
        var templateRect = initialTemplateDoc.GetPage(1).GetPageSize();
        var pageSize = new PageSize(templateRect.GetWidth(), templateRect.GetHeight());

        // Create the output PDF document
        using var writer = new PdfWriter(outputPdfPath);
        using var outputPdfDoc = new PdfDocument(writer);

        // Pre-copy template XObjects so they're reused
        var firstTemplateXObject = initialTemplateDoc.GetPage(1).CopyAsFormXObject(outputPdfDoc);
        var contTemplateXObject = continuationTemplateDoc.GetPage(1).CopyAsFormXObject(outputPdfDoc);

        // Process each page of the text PDF
        for (var i = 1; i <= totalTextPages; i++)
        {
            // Add a new page to the output document
            var newPage = outputPdfDoc.AddNewPage(pageSize);
            var canvas = new PdfCanvas(newPage);

            // Choose the correct template: initial for the first page, continuation for others
            var useFirstTemplate = i == 1;
            var templateXObject = useFirstTemplate ? firstTemplateXObject : contTemplateXObject;
            var appliedTemplatePath = useFirstTemplate ? firstTemplatePath : continuationTemplatePath;

            // Import the corresponding text page as a form XObject
            var textXObject = textPdfDoc.GetPage(i).CopyAsFormXObject(outputPdfDoc);

            // First, draw the template, then overlay the text
            canvas.AddXObjectAt(templateXObject, 0, 0);

            var topMarginPoints = useFirstTemplate
                ? _overlayOptions.FirstPageTopMarginPoints
                : _overlayOptions.ContinuationTopMarginPoints;

            var bottomMarginPoints = useFirstTemplate
                ? _overlayOptions.FirstPageBottomMarginPoints
                : _overlayOptions.ContinuationBottomMarginPoints;

            // Calculate vertical translation so content is pushed below header
            var translateY = topMarginPoints > 0 ? -topMarginPoints : 0f;

            if (topMarginPoints > 0 || bottomMarginPoints > 0)
            {
                // Apply clipping so content does not draw into header or footer areas
                var pageWidth = pageSize.GetWidth();
                var pageHeight = pageSize.GetHeight();
                const float clipX = 0f;
                var clipHeight = pageHeight - topMarginPoints - bottomMarginPoints;

                if (clipHeight > 0)
                {
                    canvas.SaveState();
                    canvas.Rectangle(clipX, bottomMarginPoints, pageWidth, clipHeight);
                    canvas.Clip();
                    canvas.EndPath();

                    // Draw the content XObject within the clipped region with vertical translation
                    canvas.AddXObjectAt(textXObject, 0, translateY);

                    canvas.RestoreState();
                }
                else
                {
                    // If margins exceed page height, fall back to drawing without clipping
                    canvas.AddXObjectAt(textXObject, 0, translateY);
                }
            }
            else
            {
                canvas.AddXObjectAt(textXObject, 0, translateY);
            }

            // Log the applied template for this page
            logger.LogInformation("Applied template to page {Page}: {Template}", i, appliedTemplatePath);
        }
    }
}
