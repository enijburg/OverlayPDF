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
}
