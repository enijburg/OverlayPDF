using Microsoft.Extensions.Logging;
using OverlayPDF.Markdown;
using Xunit;

namespace OverlayPDF.Tests;

/// <summary>
/// Tests for <see cref="MarkdownProcessor"/>.
/// </summary>
public class MarkdownProcessorTests
{
    private static MarkdownProcessor CreateProcessor()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var timelineRenderer = new TimelineRenderer();
        var signatureBlockRenderer = new SignatureBlockRenderer();
        var flowchartRenderer = new FlowchartRenderer();
        var mermaidRenderer = new MermaidRenderer(
            loggerFactory.CreateLogger<MermaidRenderer>(), flowchartRenderer);
        return new MarkdownProcessor(timelineRenderer, signatureBlockRenderer, mermaidRenderer);
    }

    [Fact]
    public void ProcessMarkdown_ShortCodeBlockLines_AreNotWrapped()
    {
        // Arrange
        var processor = CreateProcessor();
        // 94 chars = CodeBlockMaxLineChars (95) - 1; should not trigger wrapping
        var shortLine = new string('x', 94);
        var markdown = $"```json\n{shortLine}\n```";

        // Act
        var result = processor.ProcessMarkdown(markdown);

        // Assert: the original line should be present unchanged
        Assert.Contains(shortLine, result);
        // No continuation marker should have been added
        Assert.DoesNotContain("\u21B5", result);
    }

    [Fact]
    public void ProcessMarkdown_LongCodeBlockLine_IsWrappedWithContinuationMarker()
    {
        // Arrange
        var processor = CreateProcessor();
        // 100 chars > CodeBlockMaxLineChars (95); should trigger wrapping
        var longLine = new string('a', 100);
        var markdown = $"```json\n{longLine}\n```";

        // Act
        var result = processor.ProcessMarkdown(markdown);

        // Assert: the continuation marker should appear on the continuation line
        Assert.Contains("\u21B5 ", result); // ↵ followed by space
        // The first 95 characters of the original line should be present
        Assert.Contains(new string('a', 95), result);
    }

    [Fact]
    public void ProcessMarkdown_VeryLongCodeBlockLine_IsWrappedIntoMultipleChunks()
    {
        // Arrange
        var processor = CreateProcessor();
        // 200 chars = more than 2× CodeBlockMaxLineChars (95); needs at least 2 continuation lines
        var longLine = new string('b', 200);
        var markdown = $"```csharp\n{longLine}\n```";

        // Act
        var result = processor.ProcessMarkdown(markdown);

        // Assert: at least two continuation markers should appear
        Assert.True(
            result.Split('\n').Count(l => l.StartsWith("\u21B5 ")) >= 2,
            "Expected at least two continuation lines for a 200-char line");
    }

    [Fact]
    public void ProcessMarkdown_LongCodeBlockLine_DoesNotChangeLinesBelowThreshold()
    {
        // Arrange
        var processor = CreateProcessor();
        var shortLine = "short line";
        // 100 chars > CodeBlockMaxLineChars (95)
        var longLine = new string('c', 100);
        var markdown = $"```\n{shortLine}\n{longLine}\n```";

        // Act
        var result = processor.ProcessMarkdown(markdown);

        // Assert: the short line should still be present
        Assert.Contains(shortLine, result);
        // The long line should have been split
        Assert.Contains("\u21B5 ", result);
    }

    [Fact]
    public void ProcessMarkdown_SpecialBlocks_AreNotAffectedByCodeWrapping()
    {
        // Arrange – a signatures block should be processed normally and not have a ↵ injected
        var processor = CreateProcessor();
        var markdown = """
            ```signatures
            ## Sigs

            | Field | Approver |
            |-------|----------|
            | **Name** | ... |
            ```
            """;

        // Act
        var result = processor.ProcessMarkdown(markdown);

        // Assert: no continuation marker should appear (the signatures block's lines are short)
        Assert.DoesNotContain("\u21B5", result);
    }
}
