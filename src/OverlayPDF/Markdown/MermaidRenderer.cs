using Microsoft.Extensions.Logging;

namespace OverlayPDF.Markdown;

/// <summary>
/// Renders Mermaid diagram definitions to inline SVG using native renderers.
/// Flowchart diagrams are rendered via <see cref="FlowchartRenderer"/>.
/// Unsupported diagram types are returned as preformatted HTML fallbacks.
/// </summary>
public class MermaidRenderer(ILogger<MermaidRenderer> logger, FlowchartRenderer flowchartRenderer)
{
    /// <summary>
    /// Converts a Mermaid diagram definition to an inline SVG string.
    /// </summary>
    /// <param name="mermaidDefinition">The raw Mermaid markup (without the fenced code block delimiters).</param>
    /// <returns>An SVG element that can be embedded in HTML, or a <c>&lt;pre&gt;</c> fallback on failure.</returns>
    public string RenderToSvg(string mermaidDefinition)
    {
        if (string.IsNullOrWhiteSpace(mermaidDefinition))
            return string.Empty;

        if (FlowchartRenderer.CanRender(mermaidDefinition))
        {
            try
            {
                return flowchartRenderer.RenderToSvg(mermaidDefinition);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogWarning(ex, "Native flowchart rendering failed");
                return FallbackHtml(mermaidDefinition);
            }
        }

        logger.LogWarning("Unsupported Mermaid diagram type — only flowchart is supported natively");
        return FallbackHtml(mermaidDefinition);
    }

    private static string FallbackHtml(string mermaidDefinition) =>
        $"<pre>{System.Security.SecurityElement.Escape(mermaidDefinition)}</pre>";
}
