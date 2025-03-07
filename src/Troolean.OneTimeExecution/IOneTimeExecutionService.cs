namespace Troolean.OneTimeExecution;

public interface IOneTimeExecutionService
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}