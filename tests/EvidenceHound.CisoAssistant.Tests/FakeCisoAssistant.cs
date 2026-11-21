using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Scope;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireMock.Server;

namespace EvidenceHound.CisoAssistant.Tests;

/// <summary>A WireMock CISO Assistant and a client wired to it exactly as the CLI wires the real one.</summary>
internal sealed class FakeCisoAssistant : IDisposable
{
    public const string Token = "example-token-0123456789abcdef";

    private readonly ServiceProvider _services;

    public FakeCisoAssistant()
    {
        Server = WireMockServer.Start();
        _services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddCisoAssistantClient(new CisoAssistantOptions(new Uri($"{Server.Url}/api/"), Token) { RetryDelay = TimeSpan.FromMilliseconds(1) })
            .BuildServiceProvider();
        Client = _services.GetRequiredService<ICisoAssistantClient>();
        Scope = _services.GetRequiredService<IScopeStateSource>();
    }

    public WireMockServer Server { get; }

    public RecordingLoggerProvider Logs { get; } = new();

    public ICisoAssistantClient Client { get; }

    public IScopeStateSource Scope { get; }

    public void Dispose()
    {
        _services.Dispose();
        Server.Dispose();
        Logs.Dispose();
    }
}
