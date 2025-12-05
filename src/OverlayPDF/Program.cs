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

// Configure services
builder.Services
    .AddOptions<PdfOverlayOptions>()
    .BindConfiguration(nameof(PdfOverlayOptions))
    .ValidateOnStart();

builder.Services.AddHostedService<PdfOverlayService>();

var app = builder.Build();

await app.RunAsync();