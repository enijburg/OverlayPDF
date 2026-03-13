using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OverlayPDF.Markdown;
using Xunit;
using Path = System.IO.Path;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests for page number rendering.
/// Verifies that page numbers are added to all pages except the first when <see cref="PdfOverlayOptions.AddPageNumbers"/> is true.
/// </summary>
public class PdfPageNumberTests
{
    // Multi-page markdown: uses a manual page break so the output has at least two pages.
    private const string MultiPageMarkdown = """
        # First Page

        This content is on the first page.

        ----

        # Second Page

        This content is on the second page.
        """;

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_AddPageNumbersFalse_NoPageNumberOnPage2()
    {
        var (markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath) = CreateTempPaths("no_pn");
        File.WriteAllText(markdownPath, MultiPageMarkdown);
        CreateMinimalTemplatePdf(firstTemplatePath);
        CreateMinimalTemplatePdf(contTemplatePath);

        try
        {
            CreatePdfGenerator(addPageNumbers: false)
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath);

            Assert.True(File.Exists(outputPdfPath), "Output PDF should be created");

            using var pdfDocWithout = new PdfDocument(new PdfReader(outputPdfPath));
            var totalPages = pdfDocWithout.GetNumberOfPages();
            Assert.True(totalPages >= 2, "PDF should have at least 2 pages");

            // With page numbers disabled the extracted text should be the same whether
            // page numbers are enabled or not. Verify by comparing against the enabled variant.
            var outputWithNumbers = outputPdfPath + ".with_pn.pdf";
            CreatePdfGenerator(addPageNumbers: true)
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputWithNumbers, firstTemplatePath, contTemplatePath);

            try
            {
                using var pdfDocWith = new PdfDocument(new PdfReader(outputWithNumbers));

                var page2Without = PdfTextExtractor.GetTextFromPage(pdfDocWithout.GetPage(2)).Trim();
                var page2With = PdfTextExtractor.GetTextFromPage(pdfDocWith.GetPage(2)).Trim();

                // When disabled, page 2 should have less text than when enabled (no page number).
                Assert.True(page2Without.Length < page2With.Length,
                    "Page 2 should have more text when page numbers are enabled");
            }
            finally
            {
                Cleanup(outputWithNumbers);
            }
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_AddPageNumbersTrue_PageNumberOnPage2()
    {
        var (markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath) = CreateTempPaths("with_pn");
        File.WriteAllText(markdownPath, MultiPageMarkdown);
        CreateMinimalTemplatePdf(firstTemplatePath);
        CreateMinimalTemplatePdf(contTemplatePath);

        try
        {
            CreatePdfGenerator(addPageNumbers: true)
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath);

            Assert.True(File.Exists(outputPdfPath), "Output PDF should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var totalPages = pdfDoc.GetNumberOfPages();
            Assert.True(totalPages >= 2, "PDF should have at least 2 pages");

            // The number "2" should appear as isolated text on page 2 (the page number).
            // We check that the extracted text, when split into tokens, contains "2".
            var page2Text = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(2)).Trim();
            var tokens = page2Text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("2", tokens);
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_AddPageNumbersTrue_FirstPageHasNoPageNumber()
    {
        var (markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath) = CreateTempPaths("first_no_pn");
        File.WriteAllText(markdownPath, MultiPageMarkdown);
        CreateMinimalTemplatePdf(firstTemplatePath);
        CreateMinimalTemplatePdf(contTemplatePath);

        try
        {
            var generatorWithNumbers = CreatePdfGenerator(addPageNumbers: true);
            var generatorWithoutNumbers = CreatePdfGenerator(addPageNumbers: false);

            var outputWithNumbers = outputPdfPath;
            var outputWithout = outputPdfPath + ".no_pn.pdf";

            generatorWithNumbers.GeneratePdfFromMarkdownWithTemplates(markdownPath, outputWithNumbers, firstTemplatePath, contTemplatePath);
            generatorWithoutNumbers.GeneratePdfFromMarkdownWithTemplates(markdownPath, outputWithout, firstTemplatePath, contTemplatePath);

            using var pdfWith = new PdfDocument(new PdfReader(outputWithNumbers));
            using var pdfWithout = new PdfDocument(new PdfReader(outputWithout));

            // Page 1 content should be the same whether page numbers are enabled or not.
            var page1With = PdfTextExtractor.GetTextFromPage(pdfWith.GetPage(1)).Trim();
            var page1Without = PdfTextExtractor.GetTextFromPage(pdfWithout.GetPage(1)).Trim();
            Assert.Equal(page1Without, page1With);

            // Page 2 should have extra content (the page number) when enabled.
            var page2With = PdfTextExtractor.GetTextFromPage(pdfWith.GetPage(2)).Trim();
            var page2Without = PdfTextExtractor.GetTextFromPage(pdfWithout.GetPage(2)).Trim();
            Assert.True(page2With.Length > page2Without.Length,
                "Page 2 should have more text when page numbers are enabled");
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, outputPdfPath + ".no_pn.pdf", firstTemplatePath, contTemplatePath);
        }
    }

    [Theory]
    [InlineData(PageNumberAlignment.Left)]
    [InlineData(PageNumberAlignment.Center)]
    [InlineData(PageNumberAlignment.Right)]
    public void GeneratePdfFromMarkdownWithTemplates_AddPageNumbers_AllAlignmentsProducePageNumber(
        PageNumberAlignment alignment)
    {
        var (markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath) =
            CreateTempPaths($"align_{alignment}");
        File.WriteAllText(markdownPath, MultiPageMarkdown);
        CreateMinimalTemplatePdf(firstTemplatePath);
        CreateMinimalTemplatePdf(contTemplatePath);

        try
        {
            CreatePdfGenerator(addPageNumbers: true, alignment)
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath);

            Assert.True(File.Exists(outputPdfPath), "Output PDF should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            Assert.True(pdfDoc.GetNumberOfPages() >= 2, "PDF should have at least 2 pages");

            // Page number "2" must appear on page 2 regardless of alignment.
            var page2Text = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(2)).Trim();
            var tokens = page2Text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("2", tokens);
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTemplatePath, contTemplatePath);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_DifferentAlignments_ProduceDifferentXPositions()
    {
        var (markdownPath, leftPdfPath, firstTemplatePath, contTemplatePath) = CreateTempPaths("align_cmp_left");
        var centerPdfPath = leftPdfPath.Replace("align_cmp_left", "align_cmp_center");
        var rightPdfPath  = leftPdfPath.Replace("align_cmp_left", "align_cmp_right");

        File.WriteAllText(markdownPath, MultiPageMarkdown);
        CreateMinimalTemplatePdf(firstTemplatePath);
        CreateMinimalTemplatePdf(contTemplatePath);

        try
        {
            CreatePdfGenerator(addPageNumbers: true, PageNumberAlignment.Left)
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, leftPdfPath, firstTemplatePath, contTemplatePath);
            CreatePdfGenerator(addPageNumbers: true, PageNumberAlignment.Center)
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, centerPdfPath, firstTemplatePath, contTemplatePath);
            CreatePdfGenerator(addPageNumbers: true, PageNumberAlignment.Right)
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, rightPdfPath, firstTemplatePath, contTemplatePath);

            using var leftDoc   = new PdfDocument(new PdfReader(leftPdfPath));
            using var centerDoc = new PdfDocument(new PdfReader(centerPdfPath));
            using var rightDoc  = new PdfDocument(new PdfReader(rightPdfPath));

            var leftX   = GetPageNumberXPosition(leftDoc.GetPage(2));
            var centerX = GetPageNumberXPosition(centerDoc.GetPage(2));
            var rightX  = GetPageNumberXPosition(rightDoc.GetPage(2));

            Assert.NotNull(leftX);
            Assert.NotNull(centerX);
            Assert.NotNull(rightX);

            // Left < Center < Right
            Assert.True(leftX.Value < centerX.Value,
                $"Left ({leftX}) should be less than Center ({centerX})");
            Assert.True(centerX.Value < rightX.Value,
                $"Center ({centerX}) should be less than Right ({rightX})");
        }
        finally
        {
            Cleanup(markdownPath, leftPdfPath, centerPdfPath, rightPdfPath, firstTemplatePath, contTemplatePath);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static (string markdownPath, string outputPdfPath, string firstTemplatePath, string contTemplatePath)
        CreateTempPaths(string tag)
    {
        var id = Guid.NewGuid();
        var tmp = Path.GetTempPath();
        return (
            Path.Combine(tmp, $"test_pn_{tag}_{id}.md"),
            Path.Combine(tmp, $"test_pn_{tag}_{id}.pdf"),
            Path.Combine(tmp, $"test_pn_{tag}_{id}_first_tpl.pdf"),
            Path.Combine(tmp, $"test_pn_{tag}_{id}_cont_tpl.pdf")
        );
    }

    /// <summary>Creates a minimal single-page A4 PDF to serve as a template.</summary>
    private static void CreateMinimalTemplatePdf(string path)
    {
        using var writer = new PdfWriter(path);
        using var pdfDoc = new PdfDocument(writer);
        pdfDoc.AddNewPage(PageSize.A4);
    }

    private static void Cleanup(params string[] paths)
    {
        foreach (var path in paths)
            if (File.Exists(path))
                File.Delete(path);
    }

    private static PdfGenerator CreatePdfGenerator(bool addPageNumbers,
        PageNumberAlignment alignment = PageNumberAlignment.Center)
    {
        var options = Options.Create(new PdfOverlayOptions
        {
            TemplateDirectory = Path.GetTempPath(),
            DefaultFontFamily = "Helvetica",
            FontsDirectory = null,
            FirstPageTemplate = "dummy.pdf",
            ContinuationPageTemplate = "dummy.pdf",
            AddPageNumbers = addPageNumbers,
            PageNumberAlignment = alignment
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

    /// <summary>
    /// Extracts the x-coordinate of the page number text at the bottom of the page.
    /// Returns null if no text is found near the expected page number y-position.
    /// </summary>
    private static float? GetPageNumberXPosition(PdfPage page)
    {
        var strategy = new TextPositionStrategy();
        var processor = new PdfCanvasProcessor(strategy);
        processor.ProcessPageContent(page);
        return strategy.X;
    }
}
