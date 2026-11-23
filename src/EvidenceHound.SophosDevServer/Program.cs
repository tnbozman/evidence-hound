using EvidenceHound.SophosDevServer;

var parsed = DevServerOptions.Parse(args);
if (parsed is not DevServerOptions options)
{
    await Console.Error.WriteLineAsync(parsed.ToString());
    return 2;
}

using var server = DevSophosServer.Start(options.Tenant, options.Port, options.PublicUrl);
var deviations = options.Deviations.Count == 0 ? "none" : string.Join(", ", options.Deviations);
Console.WriteLine($"Example Pty Ltd Sophos Central dev tenant ({options.Variant}; deviations: {deviations})");
Console.WriteLine($"Listening on port {options.Port}; the collector reaches it at {server.Url}");
Console.WriteLine("Settings for a rest collector with the sophos-central profile (the secret client_secret can be any text):");
foreach (var (key, value) in SophosDevTenant.Settings(server.Url))
{
    Console.WriteLine($"  {key}: \"{value}\"");
}

Console.WriteLine("Ctrl+C stops the server.");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, stop.Token);
}
catch (OperationCanceledException)
{
    // Ctrl+C: stop serving.
}

return 0;
