using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OverlayPDF.Markdown;
using Xunit;
using Path = System.IO.Path;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests for PdfGenerator's PDF bookmark (outline) generation.
/// Verifies that PDF bookmarks are created from markdown headings so that the
/// TOC sidebar is populated and heading entries are navigable in PDF viewers.
/// </summary>
public class PdfGeneratorBookmarksTests
{
    // Markdown with a two-level heading hierarchy so we can verify nesting and titles.
    private const string MarkdownWithHeadings = """
        # Document Title

        ## Introduction

        Some introductory text.

        ## Overview

        Some overview text.

        ### Details

        Detail text under Overview.

        ## Conclusion

        Closing remarks.
        """;

    [Fact]
    public void GeneratePdfFromMarkdown_WithHeadings_OutlineIsCreated()
    {
        var (markdownPath, outputPdfPath) = CreateTempPaths("outline");
        File.WriteAllText(markdownPath, MarkdownWithHeadings);

        try
        {
            CreatePdfGenerator().GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            Assert.True(File.Exists(outputPdfPath), "PDF file should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var outlines = pdfDoc.GetOutlines(false);
            Assert.NotNull(outlines);
            var topLevel = outlines.GetAllChildren();
            Assert.True(topLevel.Count > 0, "PDF outline should have at least one top-level entry");
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_WithHeadings_OutlineTitlesMatchHeadings()
    {
        var (markdownPath, outputPdfPath) = CreateTempPaths("titles");
        File.WriteAllText(markdownPath, MarkdownWithHeadings);

        try
        {
            CreatePdfGenerator().GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var allTitles = CollectOutlineTitles(pdfDoc.GetOutlines(false));

            Assert.Contains("Document Title", allTitles);
            Assert.Contains("Introduction", allTitles);
            Assert.Contains("Overview", allTitles);
            Assert.Contains("Details", allTitles);
            Assert.Contains("Conclusion", allTitles);
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_WithHeadings_OutlineIsHierarchical()
    {
        var (markdownPath, outputPdfPath) = CreateTempPaths("hierarchy");
        File.WriteAllText(markdownPath, MarkdownWithHeadings);

        try
        {
            CreatePdfGenerator().GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var root = pdfDoc.GetOutlines(false);

            // H1 "Document Title" should be at root level.
            var h1Entry = root.GetAllChildren().FirstOrDefault(o => o.GetTitle() == "Document Title");
            Assert.NotNull(h1Entry);

            // H2 headings should be children of the H1 entry.
            var h2Titles = h1Entry.GetAllChildren().Select(o => o.GetTitle()).ToList();
            Assert.Contains("Introduction", h2Titles);
            Assert.Contains("Overview", h2Titles);
            Assert.Contains("Conclusion", h2Titles);

            // H3 "Details" should be a child of "Overview".
            var overviewEntry = h1Entry.GetAllChildren().First(o => o.GetTitle() == "Overview");
            var h3Titles = overviewEntry.GetAllChildren().Select(o => o.GetTitle()).ToList();
            Assert.Contains("Details", h3Titles);
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdown_WithHeadings_OutlineEntriesHaveDestinations()
    {
        var (markdownPath, outputPdfPath) = CreateTempPaths("dests");
        File.WriteAllText(markdownPath, MarkdownWithHeadings);

        try
        {
            CreatePdfGenerator().GeneratePdfFromMarkdown(markdownPath, outputPdfPath, PageSize.A4);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var allOutlines = CollectAllOutlines(pdfDoc.GetOutlines(false));
            Assert.True(allOutlines.Count > 0, "Expected outline entries in PDF");

            foreach (var outline in allOutlines)
            {
                var dest = outline.GetDestination();
                Assert.NotNull(dest);
            }
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath);
        }
    }

    [Fact]
    public void ExtractHeadings_ReturnsCorrectLevelsAndIds()
    {
        var headings = PdfGenerator.ExtractHeadings(MarkdownWithHeadings);

        Assert.Equal(5, headings.Count);
        Assert.Equal((1, "Document Title", "document-title"), headings[0]);
        Assert.Equal((2, "Introduction", "introduction"), headings[1]);
        Assert.Equal((2, "Overview", "overview"), headings[2]);
        Assert.Equal((3, "Details", "details"), headings[3]);
        Assert.Equal((2, "Conclusion", "conclusion"), headings[4]);
    }

    [Fact]
    public void AddOutlines_EmptyHeadings_DoesNotCreateOutlineEntries()
    {
        var (_, outputPdfPath) = CreateTempPaths("empty");
        // Create a minimal valid PDF and add outlines to it.
        using (var writer = new PdfWriter(outputPdfPath))
        using (var pdfDoc = new PdfDocument(writer))
        {
            pdfDoc.AddNewPage();
            PdfGenerator.AddOutlines(pdfDoc, []);
        }

        try
        {
            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            // No outlines should be present (root outline may exist but has no children).
            var outlines = pdfDoc.GetOutlines(false);
            var children = outlines?.GetAllChildren() ?? [];
            Assert.Empty(children);
        }
        finally
        {
            Cleanup(outputPdfPath);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static List<string> CollectOutlineTitles(PdfOutline root)
    {
        var result = new List<string>();
        foreach (var child in root.GetAllChildren())
        {
            result.Add(child.GetTitle());
            result.AddRange(CollectOutlineTitles(child));
        }
        return result;
    }

    private static List<PdfOutline> CollectAllOutlines(PdfOutline root)
    {
        var result = new List<PdfOutline>();
        foreach (var child in root.GetAllChildren())
        {
            result.Add(child);
            result.AddRange(CollectAllOutlines(child));
        }
        return result;
    }

    private static (string markdownPath, string outputPdfPath) CreateTempPaths(string tag)
    {
        var id = Guid.NewGuid();
        return (
            Path.Combine(Path.GetTempPath(), $"test_bookmarks_{tag}_{id}.md"),
            Path.Combine(Path.GetTempPath(), $"test_bookmarks_{tag}_{id}.pdf")
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
