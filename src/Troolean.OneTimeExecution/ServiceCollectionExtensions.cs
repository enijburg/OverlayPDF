using Microsoft.Extensions.DependencyInjection;

namespace Troolean.OneTimeExecution;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOneTimeExecutionService<TService>(this IServiceCollection services)
        where TService : class, IOneTimeExecutionService
    {
        services.AddSingleton<TService>();
        services.AddHostedService<OneTimeExecutionHostedService<TService>>();
        return services;
    }
}