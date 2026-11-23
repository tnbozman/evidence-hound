using System.CommandLine;
using System.Text.Json.Nodes;
using EvidenceHound.Cli.Setup;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Onboarding.Configuration;
using EvidenceHound.Onboarding.Models;
using EvidenceHound.Runtime.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Cli;

/// <summary>
/// <c>hound onboard</c>: the onboarding assistant, the only part of hound that uses a language model. Its settings and key live in
/// onboarding.yaml, never in the deployment config, and nothing here is used by collection.
/// </summary>
internal static class OnboardCommands
{
    public const string DefaultConfig = "onboarding.yaml";
    public const string AnthropicBaseUrlSetting = "HOUND_ANTHROPIC_BASE_URL";
    public const string OpenAiBaseUrlSetting = "HOUND_OPENAI_BASE_URL";

    private static readonly JsonObject CheckSchema = new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("ok"),
        ["properties"] = new JsonObject { ["ok"] = new JsonObject { ["type"] = "boolean" } },
    };

    public static Command Build(Terminal terminal, ILoggerFactory loggers, IConfiguration configuration, GraphAccess access)
    {
        var logger = loggers.CreateLogger("hound.onboard");
        var provider = new Option<string?>("--provider") { Description = "The model provider: anthropic or openai." };
        var model = new Option<string?>("--model") { Description = "The model; empty for the provider's default in onboarding/models.yaml." };
        var file = new Option<FileInfo>("--config") { Description = $"The onboarding config to write or check. Default: ./{DefaultConfig}." };
        var check = new Option<bool>("--check") { Description = "Resolve the key and make one minimal request to confirm it works. The key is never printed." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Ask nothing but the key: take the options given and the defaults." };
        var config = new Command("config", "Choose the model, store its key in an owner-only file, and write onboarding.yaml with a reference to it.") { provider, model, file, check, yes };
        config.SetAction(async (parseResult, cancellationToken) =>
        {
            var path = parseResult.GetValue(file)?.FullName ?? Path.GetFullPath(DefaultConfig);
            return parseResult.GetValue(check)
                ? await CheckAsync(terminal, logger, configuration, path, cancellationToken)
                : await ConfigureAsync(
                    new Wizard(terminal, parseResult.GetValue(yes)), terminal, logger, configuration, path, parseResult.GetValue(provider), parseResult.GetValue(model), cancellationToken);
        });

        var onboard = new Command("onboard", "Onboard a system with the help of a language model you choose. Never used by collection.") { config, OnboardEnvironmentCommand.Build(terminal, loggers, access) };
        OnboardSystemCommand.Attach(onboard, terminal, loggers, configuration);
        return onboard;
    }

    /// <summary>Where the key file goes: <c>$XDG_CONFIG_HOME/hound/secrets/llm_api_key</c>, else <c>~/.config/hound/secrets/llm_api_key</c>.</summary>
    public static string KeyFile(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var home = configuration["XDG_CONFIG_HOME"] is { Length: > 0 } xdg ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(home, "hound", "secrets", "llm_api_key");
    }

    private static async Task<int> ConfigureAsync(
        Wizard wizard, Terminal terminal, ILogger logger, IConfiguration configuration, string path, string? givenProvider, string? givenModel, CancellationToken cancellationToken)
    {
        var providerName = await wizard.AskAsync($"Model provider ({string.Join(" or ", OnboardingConfigLoader.Providers)})", givenProvider, "anthropic", cancellationToken);
        if (OnboardingConfigLoader.ProviderOf(providerName) is not { } provider)
        {
            return Log.Problems(logger, [new Problem("onboarding.provider_unknown", $"'{providerName}' is not a provider; choose {string.Join(" or ", OnboardingConfigLoader.Providers)}.")]);
        }

        var model = await wizard.AskAsync("Model (empty for the provider's default)", givenModel, "", cancellationToken);
        var key = await wizard.AskSecretAsync($"API key for {OnboardingConfigLoader.NameOf(provider)}", cancellationToken);
        if (key.Length == 0)
        {
            return Log.Problems(logger, [new Problem("onboarding.api_key_missing", "No key was given, so nothing was written.")]);
        }

        var keyFile = KeyFile(configuration);
        WriteOwnerOnly(keyFile, key);
        await WriteConfigAsync(path, OnboardingConfigLoader.NameOf(provider), model, $"file:{keyFile}", cancellationToken);
        await terminal.Output.WriteLineAsync($"Wrote {path}; the key is in {keyFile}, readable only by you. Check it with 'hound onboard config --check'.".AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    private static void WriteOwnerOnly(string keyFile, string key)
    {
        var folder = Path.GetDirectoryName(keyFile)!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(keyFile, key);
            return;
        }

        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using (var stream = new FileStream(keyFile, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        }))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(key);
        }

        File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Sets llm.provider, llm.model and llm.api_key, keeping any other settings an existing onboarding.yaml has.</summary>
    private static async Task WriteConfigAsync(string path, string provider, string model, string keyReference, CancellationToken cancellationToken)
    {
        var root = new YamlMappingNode();
        if (File.Exists(path))
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(await File.ReadAllTextAsync(path, cancellationToken)));
            root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode ?? root;
        }

        if (!root.Children.TryGetValue(new YamlScalarNode("llm"), out var node) || node is not YamlMappingNode llm)
        {
            llm = new YamlMappingNode();
            root.Children[new YamlScalarNode("llm")] = llm;
        }

        llm.Children[new YamlScalarNode("provider")] = new YamlScalarNode(provider);
        llm.Children[new YamlScalarNode("model")] = new YamlScalarNode(model) { Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted };
        llm.Children[new YamlScalarNode("api_key")] = new YamlScalarNode(keyReference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StringWriter();
        await writer.WriteLineAsync("# hound onboard only: the language model that helps onboard systems. Never used by collection. See docs/onboarding.md.");
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        await File.WriteAllTextAsync(path, writer.ToString().Replace("\n...\n", "\n", StringComparison.Ordinal), cancellationToken);
    }

    private static async Task<int> CheckAsync(Terminal terminal, ILogger logger, IConfiguration configuration, string path, CancellationToken cancellationToken)
    {
        var loaded = await ModelDefaults.Load(ModelDefaults.ShippedPath).BindAsync(defaults => OnboardingConfigLoader.LoadAsync(path, defaults, cancellationToken));
        if (loaded is not Result<OnboardingConfig>.Ok { Value: var config })
        {
            return Log.Problems(logger, ((Result<OnboardingConfig>.Fail)loaded).Errors);
        }

        var key = CompositeSecretProvider.Standard(name => configuration[name]).Resolve(config.ApiKey);
        if (key is not Result<Secret>.Ok { Value: var secret })
        {
            return Log.Problems(logger, ((Result<Secret>.Fail)key).Errors);
        }

        var reply = await Model(config.Llm, secret, configuration).DraftAsync(
            new DraftRequest("check", "This request only checks that the key works. Reply with ok set to true.", [], CheckSchema), cancellationToken);
        if (reply is Result<DraftReply>.Fail { Errors: var problems })
        {
            return Log.Problems(logger, problems);
        }

        await terminal.Output.WriteLineAsync($"{OnboardingConfigLoader.NameOf(config.Llm.Provider)} {config.Llm.Model}: the key works.".AsMemory(), cancellationToken);
        return ExitCodes.Ok;
    }

    /// <summary>The adapter for the configured provider. A base URL setting points it elsewhere, for tests and proxies.</summary>
    internal static IDraftingModel Model(LlmSettings settings, Secret key, IConfiguration configuration) => settings.Provider switch
    {
        LlmProvider.Anthropic => new AnthropicDraftingModel(settings, key.Reveal(), BaseUrl(configuration[AnthropicBaseUrlSetting])),
        _ => new OpenAiDraftingModel(settings, key.Reveal(), BaseUrl(configuration[OpenAiBaseUrlSetting])),
    };

    private static Uri? BaseUrl(string? text) => string.IsNullOrWhiteSpace(text) ? null : new Uri(text);
}
