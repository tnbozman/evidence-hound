using System.Reflection;
using System.Text.Json.Nodes;

namespace EvidenceHound.M365DevServer;

/// <summary>
/// Material from the ASD Blueprint for Secure Cloud (© Commonwealth of Australia 2023, CC BY 4.0), pinned to one commit. The Intune
/// export is embedded as published; the attack surface reduction rules are translated from the Blueprint's Microsoft365DSC
/// configuration. <c>Blueprint/NOTICE.md</c> lists each file, its source and what was changed.
/// </summary>
public static class Blueprint
{
    public static readonly string Commit = "a6e1e5f95e6051f23d2e7d1148c8fd57a6614000";

    public static readonly string SourceUrl = $"https://github.com/ASD-Blueprint/ASD-Blueprint-for-Secure-Cloud/tree/{Commit}/static/content/files";

    /// <summary>
    /// Every attack surface reduction rule that <c>BlueprintDefenderDsc.txt</c> (resource <c>IntuneSettingCatalogASRRulesPolicyWindows10</c>)
    /// sets, by its Graph setting suffix (the lower-cased Microsoft365DSC property name), and its mode.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AsrRules { get; } = new[]
    {
        "BlockAbuseOfExploitedVulnerableSignedDrivers",
        "BlockAdobeReaderFromCreatingChildProcesses",
        "BlockAllOfficeApplicationsFromCreatingChildProcesses",
        "BlockCredentialStealingFromWindowsLocalSecurityAuthoritySubsystem",
        "BlockExecutableContentFromEmailClientAndWebmail",
        "BlockExecutableFilesRunningUnlessTheyMeetPrevalenceAgeTrustedListCriterion",
        "BlockExecutionOfPotentiallyObfuscatedScripts",
        "BlockJavaScriptOrVBScriptFromLaunchingDownloadedExecutableContent",
        "BlockOfficeApplicationsFromCreatingExecutableContent",
        "BlockOfficeApplicationsFromInjectingCodeIntoOtherProcesses",
        "BlockOfficeCommunicationAppFromCreatingChildProcesses",
        "BlockPersistenceThroughWMIEventSubscription",
        "BlockProcessCreationsFromPSExecAndWMICommands",
        "BlockRebootingMachineInSafeMode",
        "BlockUntrustedUnsignedProcessesThatRunFromUSB",
        "BlockUseOfCopiedOrImpersonatedSystemTools",
        "BlockWebshellCreationForServers",
        "BlockWin32APICallsFromOfficeMacros",
        "UseAdvancedProtectionAgainstRansomware",
    }.ToDictionary(rule => rule.ToLowerInvariant(), _ => "block", StringComparer.Ordinal);

    /// <summary>The source, licence and attribution of every Blueprint file the dev tenant uses.</summary>
    public static string Notice { get; } = Resource("blueprint/NOTICE.md");

    /// <summary>The Blueprint's "ASD Office Hardening Guidelines" settings catalog policy, as exported from Graph.</summary>
    public static JsonObject OfficeHardening() => JsonNode.Parse(Resource("blueprint/OfficeHardening.txt"))!.AsObject();

    private static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The resource {name} is missing from the build.");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
