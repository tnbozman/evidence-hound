using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound onboard</c>: onboarding a system, the environment or a profile, step by step. hound has no language model of its own: a person
/// answers its questions, or their coding assistant (set up with <c>hound agent setup</c>) drafts through <c>pending</c> and <c>decide</c>,
/// and a person approves. Nothing here is used by collection.
/// </summary>
internal static class OnboardCommands
{
    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration, GraphAccess access)
    {
        var logger = loggers.CreateLogger("hound.onboard");
        var config = new Command("config", "Removed: hound has no built-in model to configure. Use 'hound agent setup'.") { Hidden = true, TreatUnmatchedTokensAsErrors = false };
        config.SetAction(_ =>
        {
            Log.Usage(logger, Retired.Message);
            return ExitCodes.Usage;
        });

        var onboard = new Command("onboard", "Onboard a system, the environment or a profile: you answer, or your coding assistant drafts and you approve. Never used by collection.")
        {
            config, OnboardEnvironmentCommand.Build(terminal, loggers, access), OnboardProfileCommand.Build(terminal, loggers, configuration),
        };
        OnboardSystemCommand.Attach(onboard, terminal, loggers);
        return onboard;
    }
}
