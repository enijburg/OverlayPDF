using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Troolean.OneTimeExecution;
using Path = System.IO.Path;

namespace OverlayPDF;

public class PdfOverlayService(ILogger<PdfOverlayService> logger, IOptions<PdfOverlayOptions> options)
    : IOneTimeExecutionService
{
    private readonly PdfOverlayOptions _overlayOptions = options.Value;

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // NOTE: Kept the asynchronous method signature for consistency.
        // This implementation is synchronous because the iText library does not support asynchronous operations.

        var args = Environment.GetCommandLineArgs();
        // Use args[1] as the filename (adjust if needed)
        var filename = args.Length > 1 ? args[1] : string.Empty;


        if (string.IsNullOrWhiteSpace(filename)) logger.LogError("No input file specified.");

        if (!File.Exists(filename))
        {
            logger.LogError("The input file does not exist. {Filename}", filename);
            return Task.CompletedTask;
        }

        logger.LogInformation("Applying templates to: '{Filename}'", Path.GetFileName(filename));

        var outputFilename = Path.Combine(Path.GetDirectoryName(filename)!,
            $"{Path.GetFileNameWithoutExtension(filename)}_overlay{Path.GetExtension(filename)}");

        var firstTemplatePath = Path.Combine(_overlayOptions.TemplateDirectory, _overlayOptions.FirstPageTemplate);
        var continuationTemplatePath =
            Path.Combine(_overlayOptions.TemplateDirectory, _overlayOptions.ContinuationPageTemplate);

        if (!File.Exists(firstTemplatePath) || !File.Exists(continuationTemplatePath))
        {
            logger.LogError(
                "The template files do not exist. First template: {FirstTemplatePath}, Continuation template: {ContinuationTemplatePath}",
                firstTemplatePath, continuationTemplatePath);
            return Task.CompletedTask;
        }

        try
        {
            MergePdfsWithTwoTemplates(filename, outputFilename, firstTemplatePath, continuationTemplatePath);
            logger.LogInformation("New file generated: {Output}", outputFilename);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while applying the templates.");
        }

        return Task.CompletedTask;
    }

    private void MergePdfsWithTwoTemplates(string textPdfPath, string outputPdfPath, string firstTemplatePath,
        string continuationTemplatePath)
    {
        // Open the template PDFs and the text PDF.
        using var initialTemplateDoc = new PdfDocument(new PdfReader(firstTemplatePath));
        using var continuationTemplateDoc = new PdfDocument(new PdfReader(continuationTemplatePath));
        using var textPdfDoc = new PdfDocument(new PdfReader(textPdfPath));
        // Check that the text PDF has at least one page.
        var totalTextPages = textPdfDoc.GetNumberOfPages();
        if (totalTextPages < 1)
        {
            logger.LogError("The text PDF must have at least one page.");
            return;
        }

        // Get the page size from the first page of the initial template.
        var pageSize = initialTemplateDoc.GetPage(1).GetPageSize();

        // Create the output PDF document.
        using var writer = new PdfWriter(outputPdfPath);
        using var outputPdfDoc = new PdfDocument(writer);
        using var document = new Document(outputPdfDoc, new PageSize(pageSize));

        // Process each page of the text PDF.
        for (var i = 1; i <= totalTextPages; i++)
        {
            // Add a new page to the output document.
            var newPage = outputPdfDoc.AddNewPage(new PageSize(pageSize));
            var canvas = new PdfCanvas(newPage);

            // Choose the correct template: initial for the first page, continuation for others.
            var currentTemplateDoc = i == 1 ? initialTemplateDoc : continuationTemplateDoc;
            // Import the template page as a form XObject.
            var templateXObject = currentTemplateDoc.GetPage(1).CopyAsFormXObject(outputPdfDoc);

            // Import the corresponding text page as a form XObject.
            var textXObject = textPdfDoc.GetPage(i).CopyAsFormXObject(outputPdfDoc);

            // First, draw the template, then overlay the text.
            canvas.AddXObjectAt(templateXObject, 0, 0);
            canvas.AddXObjectAt(textXObject, 0, 0);
        }
    }
}