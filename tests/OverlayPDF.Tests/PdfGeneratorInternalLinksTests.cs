using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OverlayPDF.Markdown;
using Xunit;
using Path = System.IO.Path;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests for PdfGenerator's internal hyperlink navigation functionality.
/// Verifies that PDF named destinations are created for heading anchors and that
/// internal link annotations resolve to GoTo actions rather than raw URI actions.
/// </summary>
public class PdfGeneratorInternalLinksTests
{
    // Simple markdown with a two-entry table of contents pointing to two sections.
    // Markdig AutoIdentifiers (Default mode) will generate id="introduction" and id="conclusion".
    private const string MarkdownWithInternalLinks = """
        # Document

        ## Contents

        - [Introduction](#introduction)
        - [Conclusion](#conclusion)

        ## Introduction

        This is the introduction section.

        ## Conclusion

        This is the conclusion.
        """;

    [Fact]
    public void GeneratePdfFromMarkdown_WithInternalLinks_NamedDestinationsExist()
    {
        var (markdownPath, outputPdfPath) = CreateTempPaths("dests");
        File.WriteAllText(markdownPath, MarkdownWithInternalLinks);

        try
        {
            CreatePdfGenerator().GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            Assert.True(File.Exists(outputPdfPath), "PDF file should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var names = pdfDoc.GetCatalog().GetNameTree(PdfName.Dests).GetNames();
            var destNames = new HashSet<string>(names.Keys.Select(k => k.GetValue()), StringComparer.Ordinal);

            Assert.Contains("introduction", destNames);
            Assert.Contains("conclusion", destNames);
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_WithInternalLinks_NoHashUriAnnotationsRemain()
    {
        var (markdownPath, outputPdfPath) = CreateTempPaths("uri");
        File.WriteAllText(markdownPath, MarkdownWithInternalLinks);

        try
        {
            CreatePdfGenerator().GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            Assert.True(File.Exists(outputPdfPath), "PDF file should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            for (var i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                foreach (var annotation in pdfDoc.GetPage(i).GetAnnotations())
                {
                    if (annotation is not PdfLinkAnnotation link)
                        continue;

                    var action = link.GetAction();
                    if (action is null)
                        continue;

                    if (!PdfName.URI.Equals(action.Get(PdfName.S)))
                        continue;

                    var uri = action.Get(PdfName.URI)?.ToString() ?? string.Empty;
                    Assert.False(uri.StartsWith('#'),
                        $"Internal link annotation should not remain as a URI action starting with '#', but found: {uri}");
                }
            }
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_WithInternalLinks_GoToAnnotationsExist()
    {
        var (markdownPath, outputPdfPath) = CreateTempPaths("goto");
        File.WriteAllText(markdownPath, MarkdownWithInternalLinks);

        try
        {
            CreatePdfGenerator().GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            Assert.True(File.Exists(outputPdfPath), "PDF file should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var goToCount = 0;
            for (var i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                foreach (var annotation in pdfDoc.GetPage(i).GetAnnotations())
                {
                    if (annotation is not PdfLinkAnnotation link)
                        continue;

                    var action = link.GetAction();
                    if (action is not null && PdfName.GoTo.Equals(action.Get(PdfName.S)))
                        goToCount++;
                }
            }

            Assert.True(goToCount >= 2,
                $"Expected at least 2 GoTo link annotations for the two ToC entries, but found {goToCount}");
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath);
        }
    }

    private static (string markdownPath, string outputPdfPath) CreateTempPaths(string tag)
    {
        var id = Guid.NewGuid();
        return (
            Path.Combine(Path.GetTempPath(), $"test_links_{tag}_{id}.md"),
            Path.Combine(Path.GetTempPath(), $"test_links_{tag}_{id}.pdf")
        );
    }

    private static void Cleanup(params string[] paths)
    {
        foreach (var path in paths)
            if (File.Exists(path))
                File.Delete(path);
    }

    private static PdfGenerator CreatePdfGenerator()
    {
        var options = Options.Create(new PdfOverlayOptions
        {
            TemplateDirectory = Path.GetTempPath(),
            DefaultFontFamily = "Helvetica",
            FontsDirectory = null,
            FirstPageTemplate = "dummy.pdf",
            ContinuationPageTemplate = "dummy.pdf"
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var flowchartRenderer = new FlowchartRenderer();
        var mermaidRenderer = new MermaidRenderer(loggerFactory.CreateLogger<MermaidRenderer>(), flowchartRenderer);
        var markdownProcessor = new MarkdownProcessor(
            new TimelineRenderer(),
            new SignatureBlockRenderer(),
            mermaidRenderer);

        return new PdfGenerator(options, markdownProcessor, loggerFactory.CreateLogger<PdfGenerator>());
    }
}
