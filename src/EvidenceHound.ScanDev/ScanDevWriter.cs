namespace EvidenceHound.ScanDev;

/// <summary>Writes the estate's exports, one folder per scope under the output folder, replacing what an earlier write left there.</summary>
public static class ScanDevWriter
{
    public static IReadOnlyList<string> Write(ScanDevEstate estate, string root, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(estate);
        ArgumentNullException.ThrowIfNull(root);
        var written = new List<string>();
        foreach (var scan in estate.Scans)
        {
            var folder = Path.Combine(root, scan.Folder);
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".nessus", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(old);
            }

            var path = Path.Combine(folder, ScanExports.FileName(scan, today));
            File.WriteAllText(path, ScanExports.Render(scan, today));
            File.SetLastWriteTimeUtc(path, ScanExports.ScannedAt(scan, today).UtcDateTime);
            written.Add(path);
        }

        return written;
    }
}
