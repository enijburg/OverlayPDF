using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OverlayPDF.Markdown;

/// <summary>
/// Renders timeline diagrams as SVG from simple timeline markup.
/// </summary>
public partial class TimelineRenderer
{
    [GeneratedRegex("^(m|milestone)$", RegexOptions.IgnoreCase)]
    private static partial Regex MilestonePatternRegex();

    [GeneratedRegex(@"^(?i)(m|milestone)\d*$")]
    private static partial Regex MilestoneIdRegex();

    [GeneratedRegex(@"([0-9]*\.?[0-9]+)")]
    private static partial Regex NumberPatternRegex();

    /// <summary>
    /// Generates an SVG timeline diagram from timeline markup text.
    /// </summary>
    public string GenerateTimelineSvg(string timelineText)
    {
        if (string.IsNullOrWhiteSpace(timelineText)) return string.Empty;

        // Normalize and split lines
        var lines = timelineText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        var title = string.Empty;
        var sections = new List<TimelineSection>();

        List<TimelineTask>? currentTasks = null;

        // Date parsing patterns fallback
        var datePatterns = new[] { "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy" };

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("title ", StringComparison.OrdinalIgnoreCase))
            {
                title = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("dateFormat", StringComparison.OrdinalIgnoreCase))
            {
                // ignore for now; parsing uses flexible TryParse
                continue;
            }

            if (line.StartsWith("axisFormat", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("section ", StringComparison.OrdinalIgnoreCase))
            {
                var sec = line[8..].Trim();
                currentTasks = [];
                sections.Add(new TimelineSection(sec, currentTasks));
                continue;
            }

            // Task lines: Label :id, 2025-12-01, 5d
            var parts = line.Split([':'], 2);
            if (parts.Length < 2) continue;
            var label = parts[0].Trim();
            var rest = parts[1];

            // After id comma separated
            var restParts = rest.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (restParts.Length >= 2 && currentTasks != null)
            {
                var dateToken = restParts[1];
                var durationToken = restParts.Length >= 3 ? restParts[2] : "1d";

                if (DateTime.TryParseExact(dateToken, datePatterns, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) ||
                    DateTime.TryParse(dateToken, out startDate))
                {
                    var durToken = durationToken.Trim();
                    var durLower = durToken.ToLowerInvariant();

                    // detect milestone tokens like 'm' or 'milestone'
                    var isMilestone = MilestonePatternRegex().IsMatch(durLower)
                                      || MilestoneIdRegex().IsMatch(restParts[0].Trim());

                    var days = 1.0;

                    if (!isMilestone)
                    {
                        // Try to extract a floating number
                        var m = NumberPatternRegex().Match(durLower);
                        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var dd))
                        {
                            if (durLower.EndsWith('h'))
                            {
                                // interpret hours as fraction of a 24h day
                                days = dd / 24.0;
                            }
                            else
                            {
                                days = dd; // days as-is
                            }
                        }
                        else
                        {
                            // fallback: if token ends with 'h' but no number parsed, treat as 1 hour
                            if (durLower.EndsWith('h')) days = 1.0 / 24.0;
                        }
                    }

                    currentTasks.Add(new TimelineTask(label, startDate, days, isMilestone));
                }
            }
        }

        // Flatten tasks to determine date range
        var allTasks = sections.SelectMany(s => s.Tasks.Select(t => (s.SectionTitle, t.Label, t.Start, t.DurationDays, t.IsMilestone))).ToList();
        if (!allTasks.Any()) return "<pre>No tasks parsed in timeline</pre>";

        var minDate = allTasks.Min(t => t.Start);
        var maxEnd = allTasks.Max(t => t.Start.AddDays(t.DurationDays));
        var totalDaysDouble = Math.Max(1.0, (maxEnd - minDate).TotalDays);

        // SVG layout
        const int svgWidth = 900;
        const int leftLabelWidth = 200;
        const int rightMargin = 20;
        const int timelineWidth = svgWidth - leftLabelWidth - rightMargin;
        var dayWidth = Math.Max(1.0, timelineWidth / totalDaysDouble);
        const int rowHeight = 28;
        const int headerHeight = 40;
        var totalRows = sections.Sum(s => s.Tasks.Count) + sections.Count;
        var svgHeight = headerHeight + Math.Max(200, totalRows * rowHeight + 40);

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">", ToInv(svgWidth), ToInv(svgHeight)));
        sb.AppendLine("<style> .label { font: 12px sans-serif; fill: #222; } .section { font: bold 13px sans-serif; fill: #111; } .task { fill: #4285f4; } .milestone { fill: #d93025; } .axis { font: 11px sans-serif; fill: #333; } </style>");

        // Title
        if (!string.IsNullOrEmpty(title))
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<text x=\"{0}\" y=\"20\" class=\"section\">{1}</text>", ToInv(leftLabelWidth), System.Security.SecurityElement.Escape(title)));
        }

        // Draw axis (days)
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<g transform=\"translate({0}, {1})\">", ToInv(leftLabelWidth), ToInv(headerHeight - 8)));
        var totalDaysInt = (int)Math.Ceiling(totalDaysDouble);
        var tickStep = totalDaysInt <= 31 ? 1 : Math.Max(1, (int)Math.Ceiling(totalDaysInt / 10.0));
        for (int d = 0; d <= totalDaysInt; d += tickStep)
        {
            var date = minDate.AddDays(d);
            var x = d * dayWidth;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<line x1=\"{0}\" y1=\"0\" x2=\"{0}\" y2=\"8\" stroke=\"#ccc\" />", ToInv(x)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<text x=\"{0}\" y=\"20\" class=\"axis\">{1}</text>", ToInv(x + 2), System.Security.SecurityElement.Escape(date.ToString("MMM d", CultureInfo.InvariantCulture))));
        }
        sb.AppendLine("</g>");

        // Draw tasks
        double currentY = headerHeight + 10;
        foreach (var sec in sections)
        {
            // Section title
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<text x=\"10\" y=\"{0}\" class=\"section\">{1}</text>", ToInv(currentY + 12), System.Security.SecurityElement.Escape(sec.SectionTitle)));
            currentY += rowHeight;

            foreach (var task in sec.Tasks)
            {
                var x = leftLabelWidth + (task.Start - minDate).TotalDays * dayWidth;
                var w = Math.Max(2.0, task.DurationDays * dayWidth);
                var y = currentY - (rowHeight / 2.0);

                // Label
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<text x=\"10\" y=\"{0}\" class=\"label\">{1}</text>", ToInv(y + 12), System.Security.SecurityElement.Escape(task.Label)));

                if (task.IsMilestone)
                {
                    // diamond representing milestone
                    var cx = x + w / 2.0;
                    var cy = y + 8;
                    const double size = 8.0;

                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<polygon points=\"{0},{1} {2},{3} {4},{5} {6},{7}\" class=\"milestone\" />", ToInv(cx), ToInv(cy - size), ToInv(cx + size), ToInv(cy), ToInv(cx), ToInv(cy + size), ToInv(cx - size), ToInv(cy)));
                }
                else
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"12\" rx=\"3\" class=\"task\" />", ToInv(x), ToInv(y), ToInv(w)));

                    // Date label on the bar (white)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "<text x=\"{0}\" y=\"{1}\" font-size=\"10px\" fill=\"#fff\">{2}</text>", ToInv(x + 4), ToInv(y + 10), System.Security.SecurityElement.Escape(task.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
                }

                currentY += rowHeight;
            }
        }

        sb.AppendLine("</svg>");

        return sb.ToString();

        static string ToInv(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private sealed record TimelineSection(string SectionTitle, List<TimelineTask> Tasks);

    private sealed record TimelineTask(string Label, DateTime Start, double DurationDays, bool IsMilestone);
}
