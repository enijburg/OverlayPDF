using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Navigation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OverlayPDF.Markdown;
using Xunit;
using Path = System.IO.Path;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests that verify TOC (Table of Contents) links and named destinations work correctly
/// when generating PDFs from markdown with template overlays applied.
/// This guards against the bug where named destination page references were not remapped
/// from the intermediate content PDF to the final output PDF, causing all TOC links to
/// silently fail in the template-overlayed output.
/// </summary>
public class PdfGeneratorTemplateLinksTests
{
    // Markdown with a table of contents pointing to multiple sections.
    // Markdig AutoIdentifiers (GitHub mode) generates id="introduction" and id="conclusion".
    private const string MarkdownWithToc = """
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
    public void GeneratePdfFromMarkdownWithTemplates_NamedDestinationsExist()
    {
        var (markdownPath, outputPdfPath, firstTpl, contTpl) = CreateTempPaths("tpl_dests");
        File.WriteAllText(markdownPath, MarkdownWithToc);
        CreateMinimalTemplatePdf(firstTpl);
        CreateMinimalTemplatePdf(contTpl);

        try
        {
            CreatePdfGenerator()
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTpl, contTpl);

            Assert.True(File.Exists(outputPdfPath), "Output PDF should be created");

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var names = pdfDoc.GetCatalog().GetNameTree(PdfName.Dests).GetNames();
            var destNames = new HashSet<string>(names.Keys.Select(k => k.GetValue()), StringComparer.Ordinal);

            Assert.Contains("introduction", destNames);
            Assert.Contains("conclusion", destNames);
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTpl, contTpl);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_NamedDestinationsPointToValidPages()
    {
        var (markdownPath, outputPdfPath, firstTpl, contTpl) = CreateTempPaths("tpl_valid_pages");
        File.WriteAllText(markdownPath, MarkdownWithToc);
        CreateMinimalTemplatePdf(firstTpl);
        CreateMinimalTemplatePdf(contTpl);

        try
        {
            CreatePdfGenerator()
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTpl, contTpl);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var totalPages = pdfDoc.GetNumberOfPages();
            var names = pdfDoc.GetCatalog().GetNameTree(PdfName.Dests).GetNames();

            foreach (var (key, value) in names)
            {
                Assert.IsType<PdfArray>(value);
                var destArray = (PdfArray)value;
                Assert.True(destArray.Size() >= 2,
                    $"Named destination '{key.GetValue()}' should have at least 2 elements (page ref + type)");

                // The first element must be a page dictionary that belongs to this document.
                var pageObj = destArray.Get(0);
                Assert.IsType<PdfDictionary>(pageObj);
                var pageNum = pdfDoc.GetPageNumber((PdfDictionary)pageObj);
                Assert.True(pageNum >= 1 && pageNum <= totalPages,
                    $"Named destination '{key.GetValue()}' should reference a valid page (1–{totalPages}), but got {pageNum}");
            }
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTpl, contTpl);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_GoToAnnotationsExist()
    {
        var (markdownPath, outputPdfPath, firstTpl, contTpl) = CreateTempPaths("tpl_goto");
        File.WriteAllText(markdownPath, MarkdownWithToc);
        CreateMinimalTemplatePdf(firstTpl);
        CreateMinimalTemplatePdf(contTpl);

        try
        {
            CreatePdfGenerator()
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTpl, contTpl);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var goToCount = 0;
            for (var i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                foreach (var annotation in pdfDoc.GetPage(i).GetAnnotations())
                {
                    if (annotation is not PdfLinkAnnotation link) continue;
                    var action = link.GetAction();
                    if (action is not null && PdfName.GoTo.Equals(action.Get(PdfName.S)))
                        goToCount++;
                }
            }

            Assert.True(goToCount >= 2,
                $"Expected at least 2 GoTo link annotations for the two TOC entries, but found {goToCount}");
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTpl, contTpl);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_NoHashUriAnnotationsRemain()
    {
        var (markdownPath, outputPdfPath, firstTpl, contTpl) = CreateTempPaths("tpl_uri");
        File.WriteAllText(markdownPath, MarkdownWithToc);
        CreateMinimalTemplatePdf(firstTpl);
        CreateMinimalTemplatePdf(contTpl);

        try
        {
            CreatePdfGenerator()
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTpl, contTpl);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            for (var i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
            {
                foreach (var annotation in pdfDoc.GetPage(i).GetAnnotations())
                {
                    if (annotation is not PdfLinkAnnotation link) continue;
                    var action = link.GetAction();
                    if (action is null) continue;
                    if (!PdfName.URI.Equals(action.Get(PdfName.S))) continue;

                    var uri = action.Get(PdfName.URI)?.ToString() ?? string.Empty;
                    Assert.False(uri.StartsWith('#'),
                        $"Internal link annotation should not remain as a URI action starting with '#', but found: {uri}");
                }
            }
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTpl, contTpl);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_OutlineEntriesHaveExplicitDestinations()
    {
        var (markdownPath, outputPdfPath, firstTpl, contTpl) = CreateTempPaths("tpl_outlines");
        File.WriteAllText(markdownPath, MarkdownWithToc);
        CreateMinimalTemplatePdf(firstTpl);
        CreateMinimalTemplatePdf(contTpl);

        try
        {
            CreatePdfGenerator()
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTpl, contTpl);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var root = pdfDoc.GetOutlines(false);
            Assert.NotNull(root);

            var allOutlines = CollectAllOutlines(root);
            Assert.True(allOutlines.Count > 0, "Expected outline entries in PDF");

            foreach (var outline in allOutlines)
            {
                var dest = outline.GetDestination();
                Assert.NotNull(dest);
                // Explicit destinations reference pages directly; string destinations leave
                // all entries on page 0 when the names tree is not correctly set up.
                Assert.IsType<PdfExplicitDestination>(dest);
            }
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTpl, contTpl);
        }
    }

    [Fact]
    public void GeneratePdfFromMarkdownWithTemplates_CrossPageTocLinks_AllGoToAnnotationsPreserved()
    {
        // Regression test: when pages were copied one-by-one, iText silently dropped
        // GoTo annotations whose target page had not yet been copied.  This caused
        // cross-page TOC entries (e.g. TOC on page 1 linking to a section on page 3)
        // to disappear from the output PDF.
        var filler = string.Join("\n\n", Enumerable.Range(0, 20).Select(i =>
            $"Lorem ipsum dolor sit amet paragraph {i}. " +
            "This is filler text to make the document span multiple pages. " +
            "The quick brown fox jumps over the lazy dog repeatedly."));

        var markdown = $"""
            # Document Title

            ## Table of Contents

            - [Introduction](#introduction)
            - [Chapter One](#chapter-one)
            - [Conclusion](#conclusion)

            ## Introduction

            This is the introduction section.

            {filler}

            ## Chapter One

            This is chapter one.

            {filler}

            ## Conclusion

            This is the conclusion.
            """;

        var (markdownPath, outputPdfPath, firstTpl, contTpl) = CreateTempPaths("tpl_crosspage");
        File.WriteAllText(markdownPath, markdown);
        CreateMinimalTemplatePdf(firstTpl);
        CreateMinimalTemplatePdf(contTpl);

        try
        {
            CreatePdfGenerator()
                .GeneratePdfFromMarkdownWithTemplates(markdownPath, outputPdfPath, firstTpl, contTpl);

            using var pdfDoc = new PdfDocument(new PdfReader(outputPdfPath));
            var totalPages = pdfDoc.GetNumberOfPages();
            Assert.True(totalPages >= 3,
                $"PDF should have at least 3 pages to test cross-page links, but has {totalPages}");

            // Collect all GoTo annotation destination names
            var goToDestNames = new List<string>();
            for (var i = 1; i <= totalPages; i++)
            {
                foreach (var annotation in pdfDoc.GetPage(i).GetAnnotations())
                {
                    if (annotation is not PdfLinkAnnotation link) continue;
                    var action = link.GetAction();
                    if (action is null || !PdfName.GoTo.Equals(action.Get(PdfName.S))) continue;

                    var d = action.Get(PdfName.D);
                    if (d is PdfString dStr)
                        goToDestNames.Add(dStr.GetValue());
                }
            }

            // All 3 TOC entries must have GoTo annotations — including cross-page ones
            Assert.Contains("introduction", goToDestNames);
            Assert.Contains("chapter-one", goToDestNames);
            Assert.Contains("conclusion", goToDestNames);

            // Verify that each GoTo destination resolves to a valid page via the name tree
            var names = pdfDoc.GetCatalog().GetNameTree(PdfName.Dests).GetNames();
            foreach (var destName in goToDestNames)
            {
                var found = names.Any(n => n.Key.GetValue() == destName);
                Assert.True(found, $"Named destination '{destName}' should exist in the name tree");
                var matchingValue = names.First(n => n.Key.GetValue() == destName).Value;
                Assert.IsType<PdfArray>(matchingValue);
                var destArray = (PdfArray)matchingValue;
                var pageObj = destArray.Get(0);
                Assert.IsType<PdfDictionary>(pageObj);
                var pageNum = pdfDoc.GetPageNumber((PdfDictionary)pageObj);
                Assert.True(pageNum >= 1 && pageNum <= totalPages,
                    $"GoTo dest '{destName}' should resolve to a valid page (1–{totalPages}), but got {pageNum}");
            }
        }
        finally
        {
            Cleanup(markdownPath, outputPdfPath, firstTpl, contTpl);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

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

    private static (string markdownPath, string outputPdfPath, string firstTemplatePath, string contTemplatePath)
        CreateTempPaths(string tag)
    {
        var id = Guid.NewGuid();
        var tmp = Path.GetTempPath();
        return (
            Path.Combine(tmp, $"test_tpl_links_{tag}_{id}.md"),
            Path.Combine(tmp, $"test_tpl_links_{tag}_{id}.pdf"),
            Path.Combine(tmp, $"test_tpl_links_{tag}_{id}_first_tpl.pdf"),
            Path.Combine(tmp, $"test_tpl_links_{tag}_{id}_cont_tpl.pdf")
        );
    }

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
