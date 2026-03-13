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
    /// <summary>Y-coordinate (in points) at which page numbers are rendered from the bottom of the page.</summary>
    internal const float PageNumberY = 18f;

    /// <summary>
    /// Draws a page number at the bottom of <paramref name="page"/> using the specified alignment.
    /// </summary>
    /// <param name="leftMargin">Left margin in points, used when <paramref name="alignment"/> is <see cref="PageNumberAlignment.Left"/>.</param>
    /// <param name="rightMargin">Right margin in points, used when <paramref name="alignment"/> is <see cref="PageNumberAlignment.Right"/>.</param>
    internal static void AddPageNumber(PdfDocument pdfDoc, PdfPage page, int pageNumber,
        PageNumberAlignment alignment = PageNumberAlignment.Center,
        float leftMargin = 60f, float rightMargin = 60f)
    {
        const float fontSize = 9f;

        var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var text = pageNumber.ToString();
        var pageWidth = page.GetPageSize().GetWidth();
        var textWidth = font.GetWidth(text, fontSize);

        var x = alignment switch
        {
            PageNumberAlignment.Left  => leftMargin,
            PageNumberAlignment.Right => pageWidth - rightMargin - textWidth,
            _                         => (pageWidth - textWidth) / 2
        };

        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdfDoc);
        canvas.BeginText()
              .SetFontAndSize(font, fontSize)
              .MoveText(x, PageNumberY)
              .ShowText(text)
              .EndText();
    }
}
