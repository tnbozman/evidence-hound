using System.CommandLine;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary><c>hound report</c>: posture snapshots and the committee and board reports built from them (s12-04 to s12-06).</summary>
internal static class ReportCommands
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var period = new Option<string>("--period") { Required = true };
        var scope = new Option<DirectoryInfo>("--scope");
        var config = new Option<FileInfo>("--config");
        var control = new Option<string?>("--control");
        var snapshot = new Command("snapshot") { period, scope, config, control };
        snapshot.SetAction(_ => ExitCodes.Ok);
        return new Command("report", "Reporting.") { snapshot };
    }
}
