using System.Text;

namespace OverlayPDF.Markdown;

/// <summary>
/// Processes signature blocks from markdown and generates HTML tables with fillable form fields.
/// </summary>
public class SignatureBlockRenderer
{
    /// <summary>
    /// Processes a signature block and generates HTML with form fields.
    /// </summary>
    public string ProcessSignatureBlock(string signaturesText)
    {
        if (string.IsNullOrWhiteSpace(signaturesText)) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"signature-block\" style=\"margin: 20px 0; page-break-inside: avoid;\">");

        // Split by horizontal rules (---) to separate multiple signature sections
        var sections = signaturesText.Split(["\n---\n", "\r\n---\r\n"], StringSplitOptions.RemoveEmptyEntries);

        foreach (var sectionText in sections)
        {
            if (string.IsNullOrWhiteSpace(sectionText)) continue;

            var lines = sectionText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            // Look for section title (lines starting with ## or ###)
            string? sectionTitle = null;
            var contentStartIndex = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("##"))
                {
                    sectionTitle = lines[i].TrimStart('#').Trim();
                    contentStartIndex = i + 1;
                    break;
                }
            }

            if (sectionTitle != null)
            {
                sb.AppendLine($"<h3 style=\"margin-top: 20px; margin-bottom: 10px;\">{System.Security.SecurityElement.Escape(sectionTitle)}</h3>");
            }

            // Parse markdown table
            var tableLines = lines.Skip(contentStartIndex).ToList();
            if (tableLines.Count >= 2) // Need at least header and separator
            {
                var html = ParseSignatureTable(tableLines, sectionTitle ?? "Unknown");
                sb.AppendLine(html);
            }
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string ParseSignatureTable(List<string> tableLines, string sectionName)
    {
        // Find the table header and separator
        var headerIndex = -1;
        var separatorIndex = -1;

        for (int i = 0; i < tableLines.Count; i++)
        {
            if (tableLines[i].Contains('|'))
            {
                if (headerIndex == -1)
                {
                    headerIndex = i;
                }
                else if (tableLines[i].Contains("--", StringComparison.Ordinal))
                {
                    separatorIndex = i;
                    break;
                }
            }
        }

        if (headerIndex == -1 || separatorIndex == -1)
        {
            return "<p>Invalid signature table format</p>";
        }

        // Parse header to get column names
        var headerCells = tableLines[headerIndex].Split('|')
            .Select(c => c.Trim())
            .ToList();

        // Remove the first and last elements if they're empty (edges of the table)
        if (headerCells.Count > 0 && string.IsNullOrEmpty(headerCells[0]))
            headerCells.RemoveAt(0);
        if (headerCells.Count > 0 && string.IsNullOrEmpty(headerCells[^1]))
            headerCells.RemoveAt(headerCells.Count - 1);

        if (headerCells.Count < 2) // Need at least Field column and one signatory column
        {
            return "<p>Invalid signature table format</p>";
        }

        var signatoryColumns = headerCells.Skip(1).ToList();

        // Parse data rows
        var dataRows = new List<List<string>>();
        for (int i = separatorIndex + 1; i < tableLines.Count; i++)
        {
            if (tableLines[i].Contains('|'))
            {
                // Split by | but keep empty strings - they represent empty cells
                var allCells = tableLines[i].Split('|').Select(c => c.Trim()).ToList();

                // Remove the first and last elements if they're empty (edges of the table)
                if (allCells.Count > 0 && string.IsNullOrEmpty(allCells[0]))
                    allCells.RemoveAt(0);
                if (allCells.Count > 0 && string.IsNullOrEmpty(allCells[^1]))
                    allCells.RemoveAt(allCells.Count - 1);

                if (allCells.Count >= 2)
                {
                    dataRows.Add(allCells);
                }
            }
        }

        // Generate HTML table
        var sb = new StringBuilder();
        sb.AppendLine("<table style=\"width: 100%; border-collapse: collapse; margin-bottom: 20px;\">");

        // Header
        sb.AppendLine("<thead><tr>");
        foreach (var header in headerCells)
        {
            var headerText = string.IsNullOrWhiteSpace(header) ? "&nbsp;" : System.Security.SecurityElement.Escape(header);
            sb.AppendLine($"<th style=\"border: 1px solid #ccc; padding: 8px; background-color: #f0f0f0; text-align: left;\">{headerText}</th>");
        }
        sb.AppendLine("</tr></thead>");

        // Body
        sb.AppendLine("<tbody>");
        foreach (var row in dataRows)
        {
            sb.AppendLine("<tr>");
            var fieldName = row[0];
            var cleanFieldName = fieldName.Replace("**", "").Trim();

            sb.AppendLine($"<td style=\"border: 1px solid #ccc; padding: 8px;\"><strong>{System.Security.SecurityElement.Escape(cleanFieldName)}</strong></td>");

            // For each signatory column, create a field placeholder
            for (int colIdx = 1; colIdx < row.Count && colIdx < headerCells.Count; colIdx++)
            {
                var cellValue = row[colIdx];
                var signatoryName = signatoryColumns[colIdx - 1];

                // Use a placeholder name for empty signatory columns
                var signatoryFieldName = string.IsNullOrWhiteSpace(signatoryName)
                    ? $"Col{colIdx}"
                    : signatoryName.Replace(" ", "");

                // Generate unique field name
                var uniqueFieldName = $"{sectionName.Replace(" ", "")}_{signatoryFieldName}_{cleanFieldName.Replace(" ", "").Replace("/", "")}";

                // Check if this is a pre-filled value or a field placeholder
                if (string.IsNullOrWhiteSpace(cellValue) || cellValue.Contains("...") || cellValue.All(c => char.IsWhiteSpace(c) || c == '.'))
                {
                    // Determine if this is a signature field
                    var isSignatureField = cleanFieldName.Contains("Signature", StringComparison.OrdinalIgnoreCase);

                    // This is a field - render as an HTML input element
                    if (isSignatureField)
                    {
                        sb.AppendLine($"<td style=\"border: 1px solid #ccc; padding: 4px;\"><input type=\"text\" name=\"{System.Security.SecurityElement.Escape(uniqueFieldName)}\" style=\"width: 98%; height: 100px; border: none; background: transparent; font-size: 10pt; padding: 2px;\" /></td>");
                    }
                    else
                    {
                        sb.AppendLine($"<td style=\"border: 1px solid #ccc; padding: 4px;\"><input type=\"text\" name=\"{System.Security.SecurityElement.Escape(uniqueFieldName)}\" style=\"width: 98%; border: none; background: transparent; font-size: 10pt; padding: 2px;\" /></td>");
                    }
                }
                else
                {
                    // Pre-filled value
                    sb.AppendLine($"<td style=\"border: 1px solid #ccc; padding: 8px;\"><span name=\"{System.Security.SecurityElement.Escape(uniqueFieldName)}\">{System.Security.SecurityElement.Escape(cellValue)}</span></td>");
                }
            }

            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");

        return sb.ToString();
    }
}
