using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Troolean.OneTimeExecution;

namespace Troolean.OneTimeExecutionTests;

public class ServiceCollectionExtensionsTest
{
    private IServiceCollection _services;

    [SetUp]
    public void Setup()
    {
        _services = new ServiceCollection();

        _services.AddSingleton(Mock.Of<IHostApplicationLifetime>());
        _services.AddLogging(configure => configure.AddSimpleConsole());
    }

    [Test]
    public void AddOneTimeExecutionService_ShouldRegisterServiceAsSingleton()
    {
        // Act
        _services.AddOneTimeExecutionService<TestOneTimeExecutionServiceMock>();

        // Assert
        using var serviceProvider = _services.BuildServiceProvider();
        var service = serviceProvider.GetService<TestOneTimeExecutionServiceMock>();
        var hostedService = serviceProvider.GetService<IHostedService>();

        Assert.Multiple(() =>
        {
            Assert.That(service, Is.Not.Null);
            Assert.That(service, Is.InstanceOf<TestOneTimeExecutionServiceMock>());
            Assert.That(hostedService, Is.Not.Null);
            Assert.That(hostedService, Is.InstanceOf<OneTimeExecutionHostedService<TestOneTimeExecutionServiceMock>>());
        });
    }

    [Test]
    public void AddOneTimeExecutionService_ShouldRegisterHostedService()
    {
        // Act
        _services.AddOneTimeExecutionService<TestOneTimeExecutionServiceMock>();

        // Assert
        using var serviceProvider = _services.BuildServiceProvider();
        var hostedService = serviceProvider.GetService<IHostedService>();

        Assert.Multiple(() =>
        {
            Assert.That(hostedService, Is.Not.Null);
            Assert.That(hostedService, Is.InstanceOf<OneTimeExecutionHostedService<TestOneTimeExecutionServiceMock>>());
        });
    }
}

public class TestOneTimeExecutionServiceMock : IOneTimeExecutionService
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}