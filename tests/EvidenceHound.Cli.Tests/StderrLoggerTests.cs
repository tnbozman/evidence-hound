using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli.Tests;

public sealed class StderrLoggerTests : IDisposable
{
    private readonly StringWriter _error = new();

    [Theory]
    [InlineData(LogLevel.Trace, "trace: hello")]
    [InlineData(LogLevel.Debug, "debug: hello")]
    [InlineData(LogLevel.Information, "info: hello")]
    [InlineData(LogLevel.Warning, "warn: hello")]
    [InlineData(LogLevel.Error, "error: hello")]
    [InlineData(LogLevel.Critical, "crit: hello")]
    public void Writes_level_and_message(LogLevel level, string expected)
    {
        using var provider = new StderrLoggerProvider(_error);

        provider.CreateLogger("hound.test").Log(level, "hello");

        _error.ToString().ShouldBe(expected + Environment.NewLine);
    }

    [Theory]
    [InlineData("Polly")]
    [InlineData("System.Net.Http.HttpClient.ICisoAssistantClient.LogicalHandler")]
    public void Retry_and_request_chatter_is_hidden_below_warnings(string category)
    {
        // s15-D5: Polly logged "Execution attempt" at information for every request, flooding stderr on each scope plan and apply.
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddLogging(logging => HoundCli.DefaultLogging(logging, _error))
            .BuildServiceProvider();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        logger.LogInformation("Execution attempt.");
        logger.LogWarning("Retrying.");

        _error.ToString().ShouldBe($"warn: Retrying.{Environment.NewLine}");
    }

    [Fact]
    public void Exception_is_summarised_on_one_line()
    {
        using var provider = new StderrLoggerProvider(_error);

        var exception = new InvalidOperationException("kaboom");

        provider.CreateLogger("hound.test").LogCritical(exception, "Unexpected failure.");

        _error.ToString().ShouldBe($"crit: Unexpected failure. (InvalidOperationException: kaboom){Environment.NewLine}");
    }

    [Fact]
    public void Debug_entries_include_the_full_exception()
    {
        using var provider = new StderrLoggerProvider(_error);

        var exception = new InvalidOperationException("kaboom");

        provider.CreateLogger("hound.test").LogDebug(exception, "Details:");

        _error.ToString().ShouldStartWith($"debug: Details:{Environment.NewLine}System.InvalidOperationException: kaboom");
    }

    [Fact]
    public void None_level_is_never_enabled()
    {
        using var provider = new StderrLoggerProvider(_error);
        var logger = provider.CreateLogger("hound.test");

        logger.IsEnabled(LogLevel.None).ShouldBeFalse();
        logger.Log(LogLevel.None, "hidden");
        logger.BeginScope("scope").ShouldBeNull();

        _error.ToString().ShouldBeEmpty();
    }

    public void Dispose() => _error.Dispose();
}
