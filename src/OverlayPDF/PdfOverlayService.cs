using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using Markdig;
using iText.Html2pdf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using iText.Layout.Font;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using Path = System.IO.Path;

namespace OverlayPDF;

public class PdfOverlayService(ILogger<PdfOverlayService> logger, IOptions<PdfOverlayOptions> options,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    private readonly PdfOverlayOptions _overlayOptions = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // This implementation is synchronous because the iText library does not support asynchronous operations.

        var args = Environment.GetCommandLineArgs();
        // Use args[1] as the filename (adjust if needed)
        var filename = args.Length > 1 ? args[1] : string.Empty;

        if (string.IsNullOrWhiteSpace(filename)) logger.LogError("No input file specified.");

        if (!File.Exists(filename))
        {
            logger.LogError("The input file does not exist. {Filename}", filename);
            return Task.CompletedTask;
        }

        logger.LogInformation("Applying templates to: '{Filename}'", Path.GetFileName(filename));

        var outputFilename = Path.Combine(Path.GetDirectoryName(filename)!,
            $"{Path.GetFileNameWithoutExtension(filename)}_overlay.pdf");

        var firstTemplatePath = Path.Combine(_overlayOptions.TemplateDirectory, _overlayOptions.FirstPageTemplate);
        var continuationTemplatePath =
            Path.Combine(_overlayOptions.TemplateDirectory, _overlayOptions.ContinuationPageTemplate);

        if (!File.Exists(firstTemplatePath) || !File.Exists(continuationTemplatePath))
        {
            logger.LogError("""
                            The template files do not exist. First template: {FirstTemplatePath},
                            Continuation template: {ContinuationTemplatePath}",
                            """, firstTemplatePath, continuationTemplatePath);
            return Task.CompletedTask;
        }

        // Log the templates that will be applied
        logger.LogInformation("""
                              Using templates. First page: {FirstTemplatePath},
                              Continuation pages: {ContinuationTemplatePath}",
                              """, firstTemplatePath, continuationTemplatePath);

        // If input is markdown, render it to a temporary PDF using the size of the first template
        var inputExtension = Path.GetExtension(filename);
        var textPdfPath = filename;
        string? tempPdfPath = null;

        if (inputExtension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var initialTemplateForSize = new PdfDocument(new PdfReader(firstTemplatePath));
                var templateRect = initialTemplateForSize.GetPage(1).GetPageSize();
                var pageSize = new PageSize(templateRect.GetWidth(), templateRect.GetHeight());

                tempPdfPath = Path.ChangeExtension(Path.GetTempFileName(), ".pdf");
                GeneratePdfFromMarkdown(filename, tempPdfPath, pageSize);
                textPdfPath = tempPdfPath;
                logger.LogInformation("Rendered markdown to temporary PDF: {TempPdf}", tempPdfPath);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to render markdown to PDF.");
                return Task.CompletedTask;
            }
        }

        try
        {
            MergePdfsWithTwoTemplates(textPdfPath, outputFilename, firstTemplatePath, continuationTemplatePath);
            logger.LogInformation("New file generated: {Output}", outputFilename);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while applying the templates.");
        }
        finally
        {
            if (tempPdfPath != null && File.Exists(tempPdfPath))
            {
                try
                {
                    File.Delete(tempPdfPath);
                }
                catch { /* ignore cleanup failures */ }
            }
        }

        hostApplicationLifetime.StopApplication();

        return Task.CompletedTask;
    }

    private void MergePdfsWithTwoTemplates(string textPdfPath, string outputPdfPath, string firstTemplatePath,
        string continuationTemplatePath)
    {
        // Open the template PDFs and the text PDF.
        using var initialTemplateDoc = new PdfDocument(new PdfReader(firstTemplatePath));
        using var continuationTemplateDoc = new PdfDocument(new PdfReader(continuationTemplatePath));
        using var textPdfDoc = new PdfDocument(new PdfReader(textPdfPath));
        // Check that the text PDF has at least one page.
        var totalTextPages = textPdfDoc.GetNumberOfPages();
        if (totalTextPages < 1)
        {
            logger.LogError("The text PDF must have at least one page.");
            return;
        }

        // Get the page size from the first page of the initial template.
        var templateRect = initialTemplateDoc.GetPage(1).GetPageSize();
        var pageSize = new PageSize(templateRect.GetWidth(), templateRect.GetHeight());

        // Create the output PDF document.
        using var writer = new PdfWriter(outputPdfPath);
        using var outputPdfDoc = new PdfDocument(writer);
        using var document = new Document(outputPdfDoc, pageSize);

        // Pre-copy template XObjects so they're reused.
        var firstTemplateXObject = initialTemplateDoc.GetPage(1).CopyAsFormXObject(outputPdfDoc);
        var contTemplateXObject = continuationTemplateDoc.GetPage(1).CopyAsFormXObject(outputPdfDoc);

        // Process each page of the text PDF.
        for (var i = 1; i <= totalTextPages; i++)
        {
            // Add a new page to the output document.
            var newPage = outputPdfDoc.AddNewPage(pageSize);
            var canvas = new PdfCanvas(newPage);

            // Choose the correct template: initial for the first page, continuation for others.
            var useFirstTemplate = i == 1;
            var templateXObject = useFirstTemplate ? firstTemplateXObject : contTemplateXObject;
            var appliedTemplatePath = useFirstTemplate ? firstTemplatePath : continuationTemplatePath;

            // Import the corresponding text page as a form XObject.
            var textXObject = textPdfDoc.GetPage(i).CopyAsFormXObject(outputPdfDoc);

            // First, draw the template, then overlay the text.
            canvas.AddXObjectAt(templateXObject, 0, 0);

            // Calculate vertical translation for continuation pages so content is pushed below header
            var translateY = 0f;
            if (!useFirstTemplate && _overlayOptions.ContinuationTopMarginPoints > 0)
            {
                // Negative Y moves content down in PDF coordinate space
                translateY = -_overlayOptions.ContinuationTopMarginPoints;
            }

            if (!useFirstTemplate && (_overlayOptions.ContinuationTopMarginPoints > 0 || _overlayOptions.ContinuationBottomMarginPoints > 0))
            {
                // Apply clipping so content does not draw into header or footer areas.
                var pageWidth = pageSize.GetWidth();
                var pageHeight = pageSize.GetHeight();
                const float clipX = 0f;
                var clipY = _overlayOptions.ContinuationBottomMarginPoints;
                var clipHeight = pageHeight - _overlayOptions.ContinuationTopMarginPoints - _overlayOptions.ContinuationBottomMarginPoints;

                canvas.SaveState();
                canvas.Rectangle(clipX, clipY, pageWidth, clipHeight);
                canvas.Clip();
                canvas.EndPath();

                // Draw the content XObject within the clipped region with vertical translation
                canvas.AddXObjectAt(textXObject, 0, translateY);

                canvas.RestoreState();
            }
            else
            {
                canvas.AddXObjectAt(textXObject, 0, translateY);
            }

            // Log the applied template for this page
            logger.LogInformation("Applied template to page {Page}: {Template}", i, appliedTemplatePath);
        }
    }

    private void GeneratePdfFromMarkdown(string markdownPath, string outputPdfPath, PageSize pageSize)
    {
        // Read markdown
        var markdown = File.ReadAllText(markdownPath);

        // Replace simple placeholders using a small interceptor helper
        markdown = ProcessMarkdownPlaceholders(markdown);

        // Convert markdown to HTML using Markdig
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var html = Markdown.ToHtml(markdown, pipeline);

        // Ensure default font-family is applied via inline CSS
        var fontFamily = _overlayOptions.DefaultFontFamily;
        var style = $$"""
                      <style>
                          body {
                              font-family: {{fontFamily}};
                              font-size: 11pt;
                              line-height: 1.45;
                              color: #222;
                              margin: 30px;
                          }

                          h1 {
                              font-size: 22pt;
                              font-weight: bold;
                              margin-top: 0;
                              margin-bottom: 12px;
                              color: #333;
                          }

                          h2 {
                              font-size: 16pt;
                              font-weight: bold;
                              margin-top: 24px;
                              margin-bottom: 10px;
                              color: #444;
                          }

                          h3 {
                              font-size: 13pt;
                              font-weight: bold;
                              margin-top: 18px;
                              margin-bottom: 6px;
                              color: #555;
                          }

                          p {
                              margin: 8px 0 14px 0;
                          }

                          /* iText-safe table styling */
                          table {
                              width: 100%;
                              border-collapse: collapse;
                              margin-bottom: 20px;
                              font-size: 10.5pt;
                          }

                          th {
                              background-color: #efefef;
                              font-weight: bold;
                              padding: 8px;
                              border-bottom: 2px solid #cccccc;
                              text-align: left;
                          }

                          td {
                              padding: 8px;
                              border-bottom: 1px solid #dddddd;
                              vertical-align: top;
                          }

                          /* Zebra striping – iText supports this only in this exact form */
                          tr.even td {
                              background-color: #f7f7f7;
                          }

                          /* Totals row – iText friendly */
                          .total-row td {
                              font-weight: bold;
                              background-color: #f2f2f2;
                              border-top: 2px solid #cccccc;
                              border-bottom: 2px solid #cccccc;
                          }

                          .footer-note {
                              font-size: 9pt;
                              color: #666;
                              margin-top: 20px;
                          }
                      </style>
                      """;


        html = style + html;

        // Convert HTML to PDF using iText pdfHTML
        using var writer = new PdfWriter(outputPdfPath);
        using var pdf = new PdfDocument(writer);
        pdf.SetDefaultPageSize(pageSize);

        var converterProperties = new ConverterProperties();
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(markdownPath)) ?? Directory.GetCurrentDirectory();
        converterProperties.SetBaseUri(baseDir);

        // Register fonts for pdfHTML. Use configured FontsDirectory or fall back to a 'fonts' subfolder under the template directory.
        var fontProvider = new FontProvider();
        fontProvider.AddStandardPdfFonts();

        var fontsDir = _overlayOptions.FontsDirectory;
        if (string.IsNullOrEmpty(fontsDir))
        {
            try
            {
                fontsDir = Path.Combine(_overlayOptions.TemplateDirectory, "fonts");
            }
            catch
            {
                fontsDir = null;
            }
        }

        if (!string.IsNullOrEmpty(fontsDir) && Directory.Exists(fontsDir))
        {
            // Register all fonts in the directory
            fontProvider.AddDirectory(fontsDir);

            // Prefer Poppins Light if present by registering matching files explicitly
            try
            {
                var poppinsLightFiles = Directory.GetFiles(fontsDir, "*.ttf", SearchOption.TopDirectoryOnly);
                foreach (var fontFile in poppinsLightFiles)
                {
                    fontProvider.AddFont(fontFile);
                }
            }
            catch
            {
                // ignore font discovery errors
            }
        }

        converterProperties.SetFontProvider(fontProvider);

        HtmlConverter.ConvertToPdf(html, pdf, converterProperties);
    }

    private static string ProcessMarkdownPlaceholders(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;

        // Replace [Date] with current date in short date format. Extend this method for more placeholders.
        var result = markdown.Replace("[Date]", DateTime.Now.ToString("d"));

        // Replace problematic Unicode characters that don't render well in PDF
        result = ReplaceProblematicUnicodeCharacters(result);

        // Normalize newlines to LF so regex matching is consistent across platforms
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");

        // Convert fenced `simple` timeline blocks into inline SVG HTML
        // Match ```simple\n...``` including content across lines
        result = Regex.Replace(result, "(?s)```timeline\\s*(.*?)```", match =>
        {
            var content = match.Groups[1].Value;
            try
            {
                var svg = GenerateTimelineSvg(content);
                return svg;
            }
            catch
            {
                return "<pre>Failed to render timeline</pre>";
            }
        }, RegexOptions.Multiline);

        // Treat Markdown horizontal rules (----) as explicit page breaks.
        // Replace lines that contain four or more hyphens with an HTML page-break div.
        const string hrPattern = @"(?m)^[ \t]*-{4,}[ \t]*(?:\n|$)";
        result = Regex.Replace(
            result,
            hrPattern,
            "<div style=\"page-break-after: always;\"></div>\n",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        // Restore platform newline if needed (use Environment.NewLine)
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
        var replacements = new Dictionary<string, string>
        {
            // Arrows - use simpler alternatives that are more compatible with PDF rendering
            { "→", "->" },         // Right arrow to simple ASCII
            { "←", "<-" },         // Left arrow to simple ASCII
            { "↑", "^" },          // Up arrow
            { "↓", "v" },          // Down arrow
            { "↔", "<->" },        // Left-right arrow
            { "⟶", "->" },         // Long right arrow
            { "⟵", "<-" },         // Long left arrow

            // Mathematical symbols - keep HTML entities for these as they're more likely to work
            { "≠", "&ne;" },       // Not equal
            { "≤", "&le;" },       // Less than or equal
            { "≥", "&ge;" },       // Greater than or equal
            { "±", "&plusmn;" },   // Plus-minus
            { "×", "&times;" },    // Multiplication
            { "÷", "&divide;" },   // Division
            { "∞", "&infin;" },    // Infinity
            { "√", "&radic;" },    // Square root

            // Currency and symbols
            { "€", "&euro;" },     // Euro
            { "£", "&pound;" },    // Pound
            { "¥", "&yen;" },      // Yen
            { "©", "&copy;" },     // Copyright
            { "®", "&reg;" },      // Registered
            { "™", "&trade;" },    // Trademark
            { "§", "&sect;" },     // Section sign

            // Punctuation
            { "\u201C", "&ldquo;" }, // Left double quote
            { "\u201D", "&rdquo;" }, // Right double quote
            { "\u2018", "&lsquo;" }, // Left single quote
            { "\u2019", "&rsquo;" }, // Right single quote
            { "\u2026", "..." },     // Ellipsis to simple dots
            { "\u2013", "-" },       // En dash to simple hyphen


            // Greek letters commonly used
            { "α", "&alpha;" },    // Alpha
            { "β", "&beta;" },     // Beta
            { "γ", "&gamma;" },    // Gamma
            { "δ", "&delta;" },    // Delta
            { "π", "&pi;" },       // Pi
            { "Ω", "&Omega;" },    // Omega

            // Miscellaneous
            { "°", "&deg;" },      // Degree symbol
            { "µ", "&micro;" },    // Micro sign
            { "¼", "&frac14;" },   // Quarter fraction
            { "½", "&frac12;" },   // Half fraction
            { "¾", "&frac34;" }    // Three quarters fraction
        };

        var result = text;

        // Apply predefined replacements
        foreach (var replacement in replacements)
        {
            result = result.Replace(replacement.Key, replacement.Value);
        }

        // More selective fallback: Only convert characters that are likely to cause rendering issues
        // Focus on characters above U+00FF (extended Latin and beyond) that are not in Latin-1 supplement
        var sb = new StringBuilder();
        foreach (var c in result)
        {
            int codePoint = c;

            // Only convert characters that are likely problematic:
            // - Above U+00FF (beyond Latin-1 supplement)
            // - Exclude common accented characters (U+00C0-U+00FF) which usually render fine
            // - Exclude basic punctuation and symbols that should work
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
        // This includes common punctuation, basic symbols, and Latin extended characters
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

    private static string GenerateTimelineSvg(string timelineText)
    {
        if (string.IsNullOrWhiteSpace(timelineText)) return string.Empty;

        // Normalize and split lines
        var lines = timelineText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        var title = string.Empty;
        var sections = new List<(string SectionTitle, List<(string Label, DateTime Start, double DurationDays, bool IsMilestone)> Tasks)>();

        List<(string Label, DateTime Start, double DurationDays, bool IsMilestone)>? currentTasks = null;

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
                sections.Add((sec, currentTasks));
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
                // restParts[0] may contain id like 'p1' or 'p1' prefixed
                // date is expected in restParts[1]
                var dateToken = restParts[1];
                var durationToken = restParts.Length >= 3 ? restParts[2] : "1d";

                if (DateTime.TryParseExact(dateToken, datePatterns, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) ||
                    DateTime.TryParse(dateToken, out startDate))
                {
                    // duration like 5d or 0.5d or 8h
                    var durToken = durationToken.Trim();
                    var durLower = durToken.ToLowerInvariant();

                    // detect milestone tokens like 'm' or 'milestone'
                    var isMilestone = Regex.IsMatch(durLower, "^(m|milestone)$", RegexOptions.IgnoreCase)
                                      || Regex.IsMatch(restParts[0].Trim(), "^(?i)(m|milestone)\\d*$");

                    var days = 1.0;

                    if (!isMilestone)
                    {
                        // Try to extract a floating number
                        var m = Regex.Match(durLower, "([0-9]*\\.?[0-9]+)");
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

                    currentTasks.Add((label, startDate, days, isMilestone));
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
        var totalRows = sections.Sum(s => s.Tasks.Count) + sections.Count; // include section title rows
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
        // Use whole-day tick steps to avoid floating-point accumulation skipping dates.
        // Show every day for shorter ranges (<=31 days); otherwise distribute up to ~10 ticks.
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
                    //var cyText = y + 12;
                    const double size = 8.0;

                    // Draw the red diamond on top of the black text
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
}