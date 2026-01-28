using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Path = System.IO.Path;
using iText.Kernel.Geom;
using OverlayPDF.Markdown;

namespace OverlayPDF;

/// <summary>
/// Background service that orchestrates PDF overlay operations.
/// </summary>
public class PdfOverlayService(
    ILogger<PdfOverlayService> logger,
    IOptions<PdfOverlayOptions> options,
    IHostApplicationLifetime hostApplicationLifetime,
    IConfiguration configuration,
    PdfGenerator pdfGenerator,
    PdfMerger pdfMerger)
    : BackgroundService
{
    private readonly PdfOverlayOptions _overlayOptions = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // This implementation is synchronous because the iText library does not support asynchronous operations.

        // Use configured InputFile, respects -t flag and other settings
        var filename = configuration["InputFile"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(filename))
        {
            logger.LogError("No input file specified.");
            return Task.CompletedTask;
        }

        if (!File.Exists(filename))
        {
            logger.LogError("The input file does not exist. {Filename}", filename);
            return Task.CompletedTask;
        }

        logger.LogInformation("Applying templates to: '{Filename}'", Path.GetFileName(filename));

        var outputFilename = Path.Combine(Path.GetDirectoryName(filename)!,
            $"{Path.GetFileNameWithoutExtension(filename)}_{configuration["FileSuffix"]}.pdf");

        var inputExtension = Path.GetExtension(filename);

        var noTemplate = string.Equals(configuration["NoTemplate"], "true", StringComparison.OrdinalIgnoreCase);

        if (noTemplate && inputExtension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            // Render markdown directly to PDF without applying templates/stationary
            try
            {
                var pageSize = PageSize.A4;
                pdfGenerator.GeneratePdfFromMarkdown(filename, outputFilename, pageSize);
                logger.LogInformation("Generated PDF from markdown without template: {Output}", outputFilename);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to generate PDF from markdown without template. Error: {Message}", e.Message);
            }

            hostApplicationLifetime.StopApplication();
            return Task.CompletedTask;
        }

        var firstTemplatePath = Path.Combine(_overlayOptions.TemplateDirectory, _overlayOptions.FirstPageTemplate);
        var continuationTemplatePath =
            Path.Combine(_overlayOptions.TemplateDirectory, _overlayOptions.ContinuationPageTemplate);

        if (!File.Exists(firstTemplatePath) || !File.Exists(continuationTemplatePath))
        {
            logger.LogError("""
                            The template files do not exist:
                            First template: {FirstTemplatePath},
                            Continuation template: {ContinuationTemplatePath}
                            """, firstTemplatePath, continuationTemplatePath);
            return Task.CompletedTask;
        }

        // Log the templates that will be applied
        logger.LogInformation("""
                              Using templates:
                              First page: {FirstTemplatePath},
                              Continuation pages: {ContinuationTemplatePath}
                              """, firstTemplatePath, continuationTemplatePath);

        // If input is markdown, render it directly with templates applied
        if (inputExtension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Generate PDF from markdown with templates applied directly
                pdfGenerator.GeneratePdfFromMarkdownWithTemplates(filename, outputFilename, firstTemplatePath, continuationTemplatePath);
                logger.LogInformation("Generated PDF from markdown with templates: {Output}", outputFilename);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to generate PDF from markdown. Error: {Message}, StackTrace: {StackTrace}", e.Message, e.StackTrace);
            }
        }
        else
        {
            // For regular PDFs, use the merger
            try
            {
                pdfMerger.MergePdfsWithTwoTemplates(filename, outputFilename, firstTemplatePath, continuationTemplatePath);
                logger.LogInformation("New file generated: {Output}", outputFilename);
            }
            catch (Exception e)
            {
                logger.LogError(e, "An error occurred while applying the templates.");
            }
        }

        hostApplicationLifetime.StopApplication();

        return Task.CompletedTask;
    }
}