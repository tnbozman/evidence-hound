using EvidenceHound.ScanDev;

var parsed = ScanDevOptions.Parse(args);
if (parsed is not ScanDevOptions options)
{
    await Console.Error.WriteLineAsync(parsed.ToString());
    return 2;
}

var written = ScanDevWriter.Write(options.Estate, options.Out, options.Today);
var deviations = options.Deviations.Count == 0 ? "none" : string.Join(", ", options.Deviations);
Console.WriteLine($"Example Pty Ltd scan exports ({options.Variant}; deviations: {deviations}), dated from {options.Today:yyyy-MM-dd}:");
foreach (var path in written)
{
    Console.WriteLine($"  {path}");
}

Console.WriteLine("Sources for the vuln-scan collector, one per scope (name, then settings):");
foreach (var source in options.Estate.Sources(options.Out))
{
    Console.WriteLine($"  {source.Name} {string.Join(' ', source.Settings.Select(s => $"{s.Key}={s.Value}"))}");
}

return 0;
