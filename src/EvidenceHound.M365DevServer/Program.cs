using EvidenceHound.M365DevServer;

if (DevServerOptions.Parse(args) is not DevServerOptions options)
{
    await Console.Error.WriteLineAsync(DevServerOptions.Parse(args).ToString());
    return 2;
}

using var server = DevGraphServer.Start(options.Tenant, options.Port);
var deviations = options.Deviations.Count == 0 ? "none" : string.Join(", ", options.Deviations);
Console.WriteLine($"Example Pty Ltd Microsoft 365 dev tenant ({options.Variant}; deviations: {deviations})");
Console.WriteLine($"Microsoft Graph answers at {server.Url} (try {server.Url}v1.0/users)");
Console.WriteLine("Collector settings for this tenant:");
foreach (var (key, value) in DevTenant.Settings())
{
    Console.WriteLine($"  {key}: \"{value}\"");
}

Console.WriteLine($"  graph_url: {server.Url}");
Console.WriteLine("Settings from the ASD Blueprint for Secure Cloud, © Commonwealth of Australia 2023, CC BY 4.0. Ctrl+C stops the server.");

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
