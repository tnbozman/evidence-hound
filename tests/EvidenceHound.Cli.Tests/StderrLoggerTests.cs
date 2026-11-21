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

    [Fact]
    public void Exception_is_summarised_on_one_line()
    {
        using var provider = new StderrLoggerProvider(_error);

        provider.CreateLogger("hound.test").LogCritical(new InvalidOperationException("kaboom"), "Unexpected failure.");

        _error.ToString().ShouldBe($"crit: Unexpected failure. (InvalidOperationException: kaboom){Environment.NewLine}");
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
