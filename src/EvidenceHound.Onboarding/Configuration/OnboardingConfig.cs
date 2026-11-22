using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;
using EvidenceHound.Onboarding.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Onboarding.Configuration;

/// <summary><c>onboarding.yaml</c>: the model hound onboard uses and where its key, deny terms, skills and profiles are. Separate from hound.yaml.</summary>
public sealed record OnboardingConfig(LlmSettings Llm, SecretReference ApiKey)
{
    public const long DefaultTokensPerSession = 2_000_000;

    public long MaxTokensPerSession { get; init; } = DefaultTokensPerSession;

    public string? DenyTermsFile { get; init; }

    public string SkillsPath { get; init; } = "/config/skills";

    public string ProfilesPath { get; init; } = "/config/profiles";
}

/// <summary>Each provider's default model, from <c>onboarding/models.yaml</c> shipped beside hound and editable without a build.</summary>
public sealed record ModelDefaults(IReadOnlyDictionary<LlmProvider, string> Models)
{
    public const string FileName = "models.yaml";

    public static string ShippedPath => Path.Combine(AppContext.BaseDirectory, "onboarding", FileName);

    public static Result<ModelDefaults> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Result.Fail<ModelDefaults>(new Problem("onboarding.models_missing", $"{path} is missing: it names each provider's default model."));
        }

        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));
        var root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        var models = (root?.Children.AsEnumerable() ?? []).Where(c => c.Key is YamlScalarNode && c.Value is YamlScalarNode)
            .Select(c => (Provider: OnboardingConfigLoader.ProviderOf(((YamlScalarNode)c.Key).Value), Model: ((YamlScalarNode)c.Value).Value))
            .Where(c => c.Provider is not null && !string.IsNullOrWhiteSpace(c.Model))
            .ToDictionary(c => c.Provider!.Value, c => c.Model!.Trim());
        return Result.Ok(new ModelDefaults(models));
    }
}

/// <summary>Reads and checks <c>onboarding.yaml</c>, reporting every problem with its file and line, never repeating a value that could be a key.</summary>
public static class OnboardingConfigLoader
{
    private static readonly string[] TopKeys = ["llm", "skills_path", "profiles_path"];
    private static readonly string[] LlmKeys = ["provider", "model", "api_key", "effort", "max_tokens_per_session", "deny_terms_file"];
    private static readonly string[] Efforts = ["low", "medium", "high", "max"];

    public static string[] Providers { get; } = ["anthropic", "openai"];

    public static LlmProvider? ProviderOf(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "anthropic" => LlmProvider.Anthropic,
        "openai" => LlmProvider.OpenAi,
        _ => null,
    };

    public static string NameOf(LlmProvider provider) => provider == LlmProvider.Anthropic ? "anthropic" : "openai";

    public static async Task<Result<OnboardingConfig>> LoadAsync(string path, ModelDefaults defaults, CancellationToken cancellationToken) =>
        File.Exists(path)
            ? Parse(await File.ReadAllTextAsync(path, cancellationToken), path, defaults)
            : Result.Fail<OnboardingConfig>(new Problem("onboarding.config_missing", $"{path} not found. Run 'hound onboard config' to choose a model and store its key."));

    public static Result<OnboardingConfig> Parse(string yaml, string source, ModelDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(defaults);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException exception)
        {
            return Result.Fail<OnboardingConfig>(new Problem("onboarding.yaml_invalid", $"{source}:{exception.Start.Line}: not valid YAML."));
        }

        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
        {
            return Result.Fail<OnboardingConfig>(new Problem("onboarding.document_not_a_map", $"{source}: onboarding.yaml must be a map with an 'llm' section."));
        }

        var problems = new List<Problem>();
        Unknown(root, TopKeys, source, problems);
        if (Child(root, "llm") is not YamlMappingNode llm)
        {
            problems.Add(new Problem("onboarding.llm_missing", $"{source}: 'llm' is required, with provider and api_key."));
            return Result.Fail<OnboardingConfig>(problems);
        }

        Unknown(llm, LlmKeys, source, problems);
        var providerNode = Child(llm, "provider") as YamlScalarNode;
        var provider = ProviderOf(providerNode?.Value);
        if (provider is null)
        {
            problems.Add(new Problem("onboarding.provider_unknown", $"{At(source, providerNode ?? (YamlNode)llm)}: llm.provider must be one of {string.Join(", ", Providers)}."));
        }

        var keyNode = Child(llm, "api_key") as YamlScalarNode;
        SecretReference? key = null;
        if (keyNode?.Value is not { Length: > 0 } keyText)
        {
            problems.Add(new Problem("onboarding.api_key_missing", $"{At(source, llm)}: llm.api_key is required: a reference such as env:HOUND_LLM_API_KEY or file:/run/secrets/llm_api_key."));
        }
        else if (SecretReference.Parse(keyText) is Result<SecretReference>.Ok { Value: var parsed })
        {
            key = parsed;
        }
        else
        {
            problems.Add(new Problem(
                "onboarding.api_key_literal",
                $"{At(source, keyNode)}: llm.api_key must be a reference (env:NAME or file:/path), never the key itself. Run 'hound onboard config' to store the key safely, and replace this key with your provider if it was shared."));
        }

        var effort = (Child(llm, "effort") as YamlScalarNode)?.Value;
        if (effort is { Length: > 0 } && !Efforts.Contains(effort, StringComparer.Ordinal))
        {
            problems.Add(new Problem("onboarding.effort_invalid", $"{At(source, Child(llm, "effort")!)}: llm.effort must be one of {string.Join(", ", Efforts)}."));
        }

        var budget = OnboardingConfig.DefaultTokensPerSession;
        if (Child(llm, "max_tokens_per_session") is YamlScalarNode budgetNode && (!long.TryParse(budgetNode.Value, out budget) || budget <= 0))
        {
            problems.Add(new Problem("onboarding.budget_invalid", $"{At(source, budgetNode)}: llm.max_tokens_per_session must be a whole number above 0."));
        }

        var model = (Child(llm, "model") as YamlScalarNode)?.Value?.Trim();
        string? resolved = null;
        if (provider is { } p)
        {
            resolved = model is { Length: > 0 } ? model : defaults.Models.GetValueOrDefault(p);
            if (resolved is null)
            {
                problems.Add(new Problem("onboarding.model_missing", $"{At(source, llm)}: llm.model is empty and {ModelDefaults.FileName} names no default for {NameOf(p)}."));
            }
        }

        return problems.Count > 0
            ? Result.Fail<OnboardingConfig>(problems)
            : Result.Ok(new OnboardingConfig(new LlmSettings(provider!.Value, resolved!, effort is { Length: > 0 } ? effort : null), key!)
            {
                MaxTokensPerSession = budget,
                DenyTermsFile = Text(llm, "deny_terms_file"),
                SkillsPath = Text(root, "skills_path") ?? "/config/skills",
                ProfilesPath = Text(root, "profiles_path") ?? "/config/profiles",
            });
    }

    private static void Unknown(YamlMappingNode map, string[] known, string source, List<Problem> problems) =>
        problems.AddRange(map.Children.Keys.OfType<YamlScalarNode>().Where(k => !known.Contains(k.Value, StringComparer.Ordinal))
            .Select(k => new Problem("onboarding.key_unknown", $"{At(source, k)}: '{k.Value}' is not a setting of onboarding.yaml. Known: {string.Join(", ", known)}.")));

    private static YamlNode? Child(YamlMappingNode map, string key) => map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string? Text(YamlMappingNode map, string key) => (Child(map, key) as YamlScalarNode)?.Value is { Length: > 0 } text ? text : null;

    private static string At(string source, YamlNode node) => $"{source}:{node.Start.Line}";
}
