using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

namespace OverlayPDF.Utilities;

/// <summary>
/// Shared helpers for drawing content directly onto PDF pages via <see cref="PdfCanvas"/>.
/// </summary>
internal static class PdfPageRenderer
{
    /// <summary>
    /// Draws a centred page number at the bottom of <paramref name="page"/>.
    /// </summary>
    internal static void AddPageNumber(PdfDocument pdfDoc, PdfPage page, int pageNumber)
    {
        const float fontSize = 9f;
        const float yPosition = 18f;

        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var text = pageNumber.ToString();
        var pageWidth = page.GetPageSize().GetWidth();
        var textWidth = font.GetWidth(text, fontSize);
        var x = (pageWidth - textWidth) / 2;

        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);
        canvas.BeginText()
              .SetFontAndSize(font, fontSize)
              .MoveText(x, yPosition)
              .ShowText(text)
              .EndText();
    }
}
