using System.Text;
using System.Text.RegularExpressions;

namespace OverlayPDF.Markdown;

/// <summary>
/// Handles markdown preprocessing, placeholder replacement, and special block transformations.
/// </summary>
public partial class MarkdownProcessor(TimelineRenderer timelineRenderer, SignatureBlockRenderer signatureBlockRenderer, MermaidRenderer mermaidRenderer)
{
    // Compiled regex patterns for better performance
    [GeneratedRegex(@"(?s)```signatures\s*(.*?)```", RegexOptions.Multiline)]
    private static partial Regex SignaturesBlockRegex();

    [GeneratedRegex(@"(?s)```timeline\s*(.*?)```", RegexOptions.Multiline)]
    private static partial Regex TimelineBlockRegex();

    [GeneratedRegex(@"(?s)```mermaid\s*(.*?)```", RegexOptions.Multiline)]
    private static partial Regex MermaidBlockRegex();

    [GeneratedRegex(@"(?m)^[ \t]*-{4,}[ \t]*(?:\n|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalRuleRegex();

    // Matches any remaining fenced code block (language tag is optional).
    // Special blocks (signatures, timeline, mermaid) must be processed before this regex is applied.
    [GeneratedRegex(@"(?s)```([^\n`]*)\n(.*?)```", RegexOptions.Multiline)]
    private static partial Regex FencedCodeBlockRegex();

    // Maximum characters per line inside a code block.
    // Based on A4 (595pt), body margin 30px ≈ 22.5pt each side, pre padding 10px ≈ 7.5pt each side,
    // and Courier 9pt where each character is ~5.4pt wide: (595 - 45 - 15) / 5.4 ≈ 99 chars.
    private const int CodeBlockMaxLineChars = 95;

    // Unicode "DOWNWARDS ARROW WITH CORNER LEFTWARDS" (↵) followed by a space.
    // Prepended to each continuation line when a code block line is wrapped.
    private const string ContinuationMarker = "\u21B5 ";
    private const int ContinuationMarkerLength = 2; // Length of ContinuationMarker string

    /// <summary>
    /// Processes markdown content by replacing placeholders and transforming special blocks.
    /// </summary>
    public string ProcessMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;

        // Replace [Date] with current date in short date format
        var result = markdown.Replace("[Date]", DateTime.Now.ToString("d"));

        // Replace problematic Unicode characters that don't render well in PDF
        result = ReplaceProblematicUnicodeCharacters(result);

        // Normalize newlines to LF so regex matching is consistent across platforms
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");

        // Convert fenced ```signatures blocks into HTML forms with AcroForm field placeholders
        result = SignaturesBlockRegex().Replace(result, match =>
        {
            var content = match.Groups[1].Value;
            try
            {
                return signatureBlockRenderer.ProcessSignatureBlock(content);
            }
            catch
            {
                return "<pre>Failed to render signature block</pre>";
            }
        });

        // Convert fenced ```timeline blocks into inline SVG HTML
        result = TimelineBlockRegex().Replace(result, match =>
        {
            var content = match.Groups[1].Value;
            try
            {
                return timelineRenderer.GenerateTimelineSvg(content);
            }
            catch
            {
                return "<pre>Failed to render timeline</pre>";
            }
        });

        // Convert fenced ```mermaid blocks into inline SVG via the Mermaid CLI
        result = MermaidBlockRegex().Replace(result, match =>
        {
            var content = match.Groups[1].Value;
            try
            {
                return mermaidRenderer.RenderToSvg(content);
            }
            catch
            {
                return "<pre>Failed to render mermaid diagram</pre>";
            }
        });

        // Treat Markdown horizontal rules (----) as explicit page breaks
        result = HorizontalRuleRegex().Replace(
            result,
            "<div style=\"page-break-after: always;\"></div>\n");

        // Wrap long lines in regular code blocks and add a continuation marker so that
        // the wrapped part is visually distinguishable.
        result = WrapCodeBlockLines(result);

        // Restore platform newline if needed
        if (Environment.NewLine != "\n")
        {
            result = result.Replace("\n", Environment.NewLine);
        }

        return result;
    }

    /// <summary>
    /// Finds all fenced code blocks in <paramref name="markdown"/> and wraps lines that exceed
    /// <see cref="CodeBlockMaxLineChars"/> characters. Each continuation line is prefixed with
    /// <see cref="ContinuationMarker"/> so that wrapped text is visually distinguishable in the PDF.
    /// </summary>
    private static string WrapCodeBlockLines(string markdown)
    {
        return FencedCodeBlockRegex().Replace(markdown, match =>
        {
            var language = match.Groups[1].Value;
            var content = match.Groups[2].Value;

            var lines = content.Split('\n');
            var sb = new StringBuilder();

            // Width available for continuation lines after prepending the marker
            var contWidth = CodeBlockMaxLineChars - ContinuationMarkerLength;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Skip the final empty element produced by a trailing '\n' in the content
                if (i == lines.Length - 1 && string.IsNullOrEmpty(line))
                    break;

                if (line.Length <= CodeBlockMaxLineChars)
                {
                    sb.Append(line);
                    sb.Append('\n');
                }
                else
                {
                    // Emit the first chunk at full width
                    sb.Append(line[..CodeBlockMaxLineChars]);
                    sb.Append('\n');

                    // Emit continuation chunks, each prefixed with the continuation marker
                    var remaining = line[CodeBlockMaxLineChars..];

                    while (remaining.Length > 0)
                    {
                        var chunkLen = Math.Min(contWidth, remaining.Length);
                        sb.Append(ContinuationMarker);
                        sb.Append(remaining[..chunkLen]);
                        sb.Append('\n');
                        remaining = remaining[chunkLen..];
                    }
                }
            }

            return $"```{language}\n{sb}```";
        });
    }

    private static string ReplaceProblematicUnicodeCharacters(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Dictionary of problematic Unicode characters and their safe replacements
        // Using explicit Unicode escape sequences to avoid encoding issues
        var replacements = new Dictionary<string, string>
        {
            // Arrows - use simpler alternatives that are more compatible with PDF rendering
            { "\u2192", "->" },         // Right arrow to simple ASCII
            { "\u2190", "<-" },         // Left arrow to simple ASCII
            { "\u2191", "^" },          // Up arrow
            { "\u2193", "v" },          // Down arrow
            { "\u2194", "<->" },        // Left-right arrow
            { "\u27F6", "->" },         // Long right arrow
            { "\u27F5", "<-" },         // Long left arrow

            // Mathematical symbols - keep HTML entities for these as they're more likely to work
            { "\u2260", "&ne;" },       // Not equal
            { "\u2264", "&le;" },       // Less than or equal
            { "\u2265", "&ge;" },       // Greater than or equal
            { "\u00B1", "&plusmn;" },   // Plus-minus
            { "\u00D7", "&times;" },    // Multiplication
            { "\u00F7", "&divide;" },   // Division
            { "\u221E", "&infin;" },    // Infinity
            { "\u221A", "&radic;" },    // Square root

            // Currency and symbols
            { "\u20AC", "&euro;" },     // Euro
            { "\u00A3", "&pound;" },    // Pound
            { "\u00A5", "&yen;" },      // Yen
            { "\u00A9", "&copy;" },     // Copyright
            { "\u00AE", "&reg;" },      // Registered
            { "\u2122", "&trade;" },    // Trademark
            { "\u00A7", "&sect;" },     // Section sign

            // Punctuation
            { "\u201C", "&ldquo;" },    // Left double quote
            { "\u201D", "&rdquo;" },    // Right double quote
            { "\u2018", "&lsquo;" },    // Left single quote
            { "\u2019", "&rsquo;" },    // Right single quote
            { "\u2026", "..." },        // Ellipsis to simple dots
            { "\u2013", "-" },          // En dash to simple hyphen

            // Greek letters commonly used
            { "\u03B1", "&alpha;" },    // Alpha
            { "\u03B2", "&beta;" },     // Beta
            { "\u03B3", "&gamma;" },    // Gamma
            { "\u03B4", "&delta;" },    // Delta
            { "\u03C0", "&pi;" },       // Pi
            { "\u03A9", "&Omega;" },    // Omega

            // Miscellaneous
            { "\u00B0", "&deg;" },      // Degree symbol
            { "\u00B5", "&micro;" },    // Micro sign
            { "\u00BC", "&frac14;" },   // Quarter fraction
            { "\u00BD", "&frac12;" },   // Half fraction
            { "\u00BE", "&frac34;" }    // Three quarters fraction
        };

        var result = text;

        // Apply predefined replacements
        foreach (var replacement in replacements)
        {
            result = result.Replace(replacement.Key, replacement.Value);
        }

        // More selective fallback: Only convert characters that are likely to cause rendering issues
        var sb = new StringBuilder();
        foreach (var c in result)
        {
            int codePoint = c;

            // Only convert characters that are likely problematic:
            // - Above U+00FF (beyond Latin-1 supplement)
            // - Exclude common accented characters (U+00C0-U+00FF) which usually render fine
            if (codePoint > 255 && !IsKnownSafeUnicode(codePoint))
            {
                sb.Append($"&#{codePoint};");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static bool IsKnownSafeUnicode(int codePoint)
    {
        // Characters that are generally safe to render in PDFs without conversion
        return codePoint switch
        {
            // Latin Extended-A (U+0100-U+017F) - usually safe
            >= 0x0100 and <= 0x017F => true,

            // Latin Extended-B (U+0180-U+024F) - usually safe
            >= 0x0180 and <= 0x024F => true,

            // Basic punctuation that might appear in this range
            0x2010 => true, // Hyphen
            0x2011 => true, // Non-breaking hyphen
            0x2012 => true, // Figure dash

            // Code block continuation marker (↵ DOWNWARDS ARROW WITH CORNER LEFTWARDS)
            0x21B5 => true,

            // Keep other characters for entity conversion
            _ => false
        };
    }
}
