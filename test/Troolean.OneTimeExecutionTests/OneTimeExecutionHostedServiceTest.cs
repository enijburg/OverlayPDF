using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Troolean.OneTimeExecution;

namespace Troolean.OneTimeExecutionTests;

public class OneTimeExecutionHostedServiceTest
{
    private OneTimeExecutionHostedService<IOneTimeExecutionService> _hostedService;
    private Mock<IOneTimeExecutionService> _mockExecutionService;
    private Mock<IHostApplicationLifetime> _mockHostApplicationLifetime;
    private Mock<ILogger<OneTimeExecutionHostedService<IOneTimeExecutionService>>> _mockLogger;

    [SetUp]
    public void Setup()
    {
        _mockExecutionService = new Mock<IOneTimeExecutionService>();
        _mockHostApplicationLifetime = new Mock<IHostApplicationLifetime>();
        _mockLogger = new Mock<ILogger<OneTimeExecutionHostedService<IOneTimeExecutionService>>>();

        _hostedService = new OneTimeExecutionHostedService<IOneTimeExecutionService>(
            _mockExecutionService.Object,
            _mockHostApplicationLifetime.Object,
            _mockLogger.Object
        );
    }

    [Test]
    public async Task StartAsync_ShouldCallExecuteAsync()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        await _hostedService.StartAsync(cancellationToken);

        // Assert
        _mockExecutionService.Verify(s => s.ExecuteAsync(cancellationToken), Times.Once);
    }

    [Test]
    public async Task StartAsync_ShouldStopApplicationAfterExecution()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;

        // Act
        await _hostedService.StartAsync(cancellationToken);

        // Assert
        _mockHostApplicationLifetime.Verify(h => h.StopApplication(), Times.Once);
    }

    [Test]
    public async Task StartAsync_ShouldLogCriticalOnException()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var exception = new Exception("Test exception");
        _mockExecutionService.Setup(s => s.ExecuteAsync(cancellationToken)).ThrowsAsync(exception);

        // Act
        await _hostedService.StartAsync(cancellationToken);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error during execution of service")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}