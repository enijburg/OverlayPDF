using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OverlayPDF;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole(options =>
        {
            options.FormatterName = "custom";
        });
        logging.AddConsoleFormatter<CustomConsoleFormatter, ConsoleFormatterOptions>();
    })
    .ConfigureServices((_, services) =>
    {
        services.AddOptions<PdfOverlayOptions>()
            .BindConfiguration(nameof(PdfOverlayOptions))
            .ValidateOnStart();

        services.AddTransient<PdfOverlayService>();
    });

using var host = builder.Build();

using var serviceScope = host.Services.CreateScope();
var services = serviceScope.ServiceProvider;

try
{
    var overlayService = services.GetRequiredService<PdfOverlayService>();
    overlayService.Execute(args[0] ?? "");
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
    return 1;
}

return 0;
