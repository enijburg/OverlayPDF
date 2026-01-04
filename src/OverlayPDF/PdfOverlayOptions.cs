namespace OverlayPDF;

public record PdfOverlayOptions
{
    public required string TemplateDirectory { get; set; }
    public required string FirstPageTemplate { get; set; }
    public required string ContinuationPageTemplate { get; set; }

    // Top margin (in points) to apply to first page so content is pushed down below headers
    public float FirstPageTopMarginPoints { get; set; } = 0f;

    // Bottom margin (in points) to reserve on first page so content does not touch footer
    public float FirstPageBottomMarginPoints { get; set; } = 0f;

    // Top margin (in points) to apply to continuation pages so content is pushed down below headers
    public float ContinuationTopMarginPoints { get; set; } = 72f;

    // Bottom margin (in points) to reserve on continuation pages so content does not touch footer
    public float ContinuationBottomMarginPoints { get; set; } = 36f;

    // Optional directory with font files (TTF/OTF). If provided, these will be registered for pdfHTML.
    public string? FontsDirectory { get; set; }

    // Default CSS font-family to use when rendering HTML (e.g. "Poppins, sans-serif").
    public required string DefaultFontFamily { get; set; }
}