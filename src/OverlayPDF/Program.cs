﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OverlayPDF;
using Troolean.OneTimeExecution;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole(options => { options.FormatterName = "custom"; });
        logging.AddConsoleFormatter<CustomConsoleFormatter, ConsoleFormatterOptions>();
    })
    .ConfigureServices((_, services) =>
    {
        services.AddOptions<PdfOverlayOptions>()
            .BindConfiguration(nameof(PdfOverlayOptions))
            .ValidateOnStart();

        services.AddOneTimeExecutionService<PdfOverlayService>();
    });

using var host = builder.Build();

await host.RunAsync();