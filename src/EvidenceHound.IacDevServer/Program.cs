using EvidenceHound.IacDevServer;

var parsed = DevServerOptions.Parse(args);
if (parsed is not DevServerOptions options)
{
    await Console.Error.WriteLineAsync(parsed.ToString());
    return 2;
}

var pipeline = options.Pipeline;
var source = "the described control tests";
if (options.ControlsReport is { } file)
{
    if (!File.Exists(file))
    {
        await Console.Error.WriteLineAsync($"No report at {file}. Make one with: terraform test -junit-xml={file}");
        return 2;
    }

    pipeline = pipeline.WithReport(await File.ReadAllTextAsync(file));
    source = file;
}

using var server = DevIacServer.Start(pipeline, DateTimeOffset.UtcNow, options.Port, options.PublicUrl);
var deviations = options.Deviations.Count == 0 ? "none" : string.Join(", ", options.Deviations);
Console.WriteLine($"Example Pty Ltd IaC pipeline, {pipeline.Repository} ({options.Variant}; deviations: {deviations}; control-test report: {source})");
Console.WriteLine($"Listening on port {options.Port}; the collector reaches it at {server.Url}");
Console.WriteLine("Settings for rest collectors with the terraform-tests (or your copy, such as grc-infra-controls) and terraform-drift profiles");
Console.WriteLine("(the secret token can be any text):");
foreach (var profile in new[] { "terraform-tests", "terraform-drift" })
{
    Console.WriteLine($"  {profile}:");
    foreach (var (key, value) in IacDevPipeline.Settings(server.Url, profile))
    {
        Console.WriteLine($"    {key}: \"{value}\"");
    }
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
