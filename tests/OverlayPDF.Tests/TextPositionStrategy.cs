using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using OverlayPDF.Utilities;

namespace OverlayPDF.Tests;

/// <summary>
/// Simple iText text extraction listener that records the x-coordinate of text
/// rendered near the bottom of the page, at the y-coordinate where page numbers
/// are drawn by <see cref="PdfPageRenderer"/>. Used by tests to verify page number
/// horizontal alignment.
/// </summary>
internal sealed class TextPositionStrategy : IEventListener
{
    /// <summary>
    /// Tolerance in points to accommodate minor floating-point differences between
    /// the MoveText y-coordinate and the baseline reported during text extraction.
    /// </summary>
    private const float YTolerance = 4f;

    /// <summary>
    /// The x-coordinate of the first character of the text found near the page-number
    /// y-coordinate, or <c>null</c> if no such text was encountered.
    /// </summary>
    public float? X { get; private set; }

    public void EventOccurred(IEventData data, EventType type)
    {
        if (type != EventType.RENDER_TEXT)
            return;

        var renderInfo = (TextRenderInfo)data;
        var baseline = renderInfo.GetBaseline();
        var y = baseline.GetStartPoint().Get(1);

        // Only capture text near the page-number row; ignore all other content.
        if (Math.Abs(y - PdfPageRenderer.PageNumberY) > YTolerance)
            return;

        if (X is null)
            X = baseline.GetStartPoint().Get(0);
    }

    public ICollection<EventType> GetSupportedEvents() =>
        [EventType.RENDER_TEXT];
}
