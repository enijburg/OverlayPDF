using System.Text;
using System.Text.RegularExpressions;

namespace OverlayPDF.Markdown;

/// <summary>
/// Handles markdown preprocessing, placeholder replacement, and special block transformations.
/// </summary>
public partial class MarkdownProcessor(TimelineRenderer timelineRenderer, SignatureBlockRenderer signatureBlockRenderer)
{
    // Compiled regex patterns for better performance
    [GeneratedRegex(@"(?s)```signatures\s*(.*?)```", RegexOptions.Multiline)]
    private static partial Regex SignaturesBlockRegex();

    [GeneratedRegex(@"(?s)```timeline\s*(.*?)```", RegexOptions.Multiline)]
    private static partial Regex TimelineBlockRegex();

    [GeneratedRegex(@"(?m)^[ \t]*-{4,}[ \t]*(?:\n|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalRuleRegex();

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

        // Treat Markdown horizontal rules (----) as explicit page breaks
        result = HorizontalRuleRegex().Replace(
            result,
            "<div style=\"page-break-after: always;\"></div>\n");

        // Restore platform newline if needed
        if (Environment.NewLine != "\n")
        {
            result = result.Replace("\n", Environment.NewLine);
        }

        return result;
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

            // Keep other characters for entity conversion
            _ => false
        };
    }
}
