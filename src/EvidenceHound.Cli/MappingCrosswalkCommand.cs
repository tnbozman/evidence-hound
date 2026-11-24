using System.CommandLine;
using EvidenceHound.Runtime.Collectors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>s12-08: <c>hound mapping crosswalk</c>.</summary>
internal static class MappingCrosswalkCommand
{
    public static Command Build(TextWriter output, ILoggerFactory loggers, IConfiguration configuration, CollectorRegistry registry)
    {
        var command = new Command("crosswalk", "Not implemented.");
        command.SetAction(_ => ExitCodes.Ok);
        return command;
    }
}
