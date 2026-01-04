using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OverlayPDF;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging using the new builder APIs
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => { options.FormatterName = "custom"; });
builder.Logging.AddConsoleFormatter<CustomConsoleFormatter, ConsoleFormatterOptions>();

// Allow selecting a named PdfOverlayOptions config section via command line:
//   OverlayPDF.exe --template template_1 <inputFile>
// If omitted, the first configured PdfOverlayOptions child section is used.
var template = GetCommandLineValue(args, "--template")
               ?? GetCommandLineValue(args, "-t");

var inputFile = GetFirstNonOptionArgument(args);

var optionsRoot = builder.Configuration.GetSection(nameof(PdfOverlayOptions));
var availableTemplates = optionsRoot.GetChildren()
    .Select(c => c.Key)
    .Where(k => !string.IsNullOrWhiteSpace(k))
    .ToArray();

var resolvedTemplate = string.IsNullOrWhiteSpace(template)
    ? availableTemplates.FirstOrDefault()
    : template;

if (string.IsNullOrWhiteSpace(resolvedTemplate))
{
    throw new InvalidOperationException($"No '{nameof(PdfOverlayOptions)}' templates were found in configuration.");
}

// Validate that the named section exists early so we fail fast with a clear message.
var namedOptionsSectionPath = $"{nameof(PdfOverlayOptions)}:{resolvedTemplate}";
var namedOptionsSection = builder.Configuration.GetSection(namedOptionsSectionPath);
if (!namedOptionsSection.Exists())
{
    throw new InvalidOperationException($"No configuration found for '{namedOptionsSectionPath}'. Update appsettings.json or pass --template <name>.");
}

// Apply overrides so the worker can construct output and locate the input file.
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["FileSuffix"] = resolvedTemplate.ToLowerInvariant(),
    ["InputFile"] = inputFile
});

// Configure services
builder.Services
    .AddOptions<PdfOverlayOptions>()
    .Bind(namedOptionsSection)
    .ValidateOnStart();

builder.Services.AddHostedService<PdfOverlayService>();

var app = builder.Build();

await app.RunAsync();
return;

static string? GetCommandLineValue(string[] args, string key)
{
    if (args.Length == 0) return null;

    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (string.Equals(a, key, StringComparison.OrdinalIgnoreCase))
        {
            return (i + 1) < args.Length ? args[i + 1] : null;
        }

        if (a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
        {
            return a[(key.Length + 1)..];
        }
    }

    return null;
}

static string? GetFirstNonOptionArgument(string[] args)
{
    if (args.Length == 0) return null;

    for (var i = 0; i < args.Length; i++)
    {
        var a = args[i];
        if (string.IsNullOrWhiteSpace(a)) continue;

        // Skip options (and their values) we know about.
        if (string.Equals(a, "--template", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-t", StringComparison.OrdinalIgnoreCase))
        {
            i++; // skip value
            continue;
        }

        if (a.StartsWith("--template=", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("-t=", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        // Any other dash-prefixed token is treated as an option; skip it.
        if (a.StartsWith('-'))
        {
            continue;
        }

        return a;
    }

    return null;
}