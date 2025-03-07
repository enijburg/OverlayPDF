using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Troolean.OneTimeExecution;

public class OneTimeExecutionHostedService<T>(T oneTimeExecutionService, IHostApplicationLifetime hostApplicationLifetime,
    ILogger<OneTimeExecutionHostedService<T>> logger) : IHostedService where T : IOneTimeExecutionService
{
    private T _oneTimeExecutionService = oneTimeExecutionService;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _oneTimeExecutionService.ExecuteAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Error during execution of service '{Service}'", nameof(T));
        }
        finally
        {
            // Terminate the application once the conversion is complete.
            hostApplicationLifetime.StopApplication();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}