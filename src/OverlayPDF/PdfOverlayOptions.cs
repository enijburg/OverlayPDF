namespace OverlayPDF;

public record PdfOverlayOptions
{
    public required string TemplateDirectory { get; set; }
    public required string FirstPageTemplate { get; set; }
    public required string ContinuationPageTemplate { get; set; }
}