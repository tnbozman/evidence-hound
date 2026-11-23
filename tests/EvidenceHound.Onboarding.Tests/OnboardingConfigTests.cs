using EvidenceHound.Onboarding.Configuration;
using EvidenceHound.Onboarding.Models;

namespace EvidenceHound.Onboarding.Tests;

public class OnboardingConfigTests
{
    private static readonly ModelDefaults Defaults = ModelDefaults.Load(Path.Combine(AppContext.BaseDirectory, "files", "onboarding", "models.yaml")).Value();

    [Fact]
    public void Literal_api_key_is_refused()
    {
        var problems = OnboardingConfigLoader.Parse("llm:\n  provider: anthropic\n  api_key: example-literal-key-value\n", "onboarding.yaml", Defaults).Problems();

        var problem = problems.ShouldHaveSingleItem();
        problem.Code.ShouldBe("onboarding.api_key_literal");
        problem.Message.ShouldContain("onboarding.yaml:3");
        problem.Message.ShouldNotContain("example-literal-key-value");
    }

    [Fact]
    public void Empty_model_uses_provider_default()
    {
        var anthropic = OnboardingConfigLoader.Parse("llm:\n  provider: anthropic\n  model: \"\"\n  api_key: env:HOUND_LLM_API_KEY\n", "onboarding.yaml", Defaults).Value();
        var openai = OnboardingConfigLoader.Parse("llm:\n  provider: openai\n  api_key: file:/run/secrets/llm_api_key\n", "onboarding.yaml", Defaults).Value();

        (anthropic.Llm.Provider, anthropic.Llm.Model).ShouldBe((LlmProvider.Anthropic, "claude-opus-5-5"));
        (openai.Llm.Provider, openai.Llm.Model).ShouldBe((LlmProvider.OpenAi, "gpt-6.2-astra"));
        anthropic.ApiKey.ToString().ShouldBe("env:HOUND_LLM_API_KEY");
    }

    [Fact]
    public void Reads_every_setting_and_reports_unknown_ones()
    {
        var config = OnboardingConfigLoader.Parse(
            """
            llm:
              provider: openai
              model: example-model
              api_key: env:HOUND_LLM_API_KEY
              effort: medium
              max_tokens_per_session: 400000
              deny_terms_file: /config/deny-terms.txt
            skills_path: /config/skills
            profiles_path: /config/profiles
            """,
            "onboarding.yaml",
            Defaults).Value();

        config.ShouldBe(config with
        {
            Llm = new LlmSettings(LlmProvider.OpenAi, "example-model", "medium"),
            MaxTokensPerSession = 400000,
            DenyTermsFile = "/config/deny-terms.txt",
            SkillsPath = "/config/skills",
            ProfilesPath = "/config/profiles",
        });
        OnboardingConfigLoader.Parse("llm:\n  provider: other\n  api_key: env:K\ncolour: blue\n", "onboarding.yaml", Defaults).Problems()
            .Select(p => p.Code).ShouldBe(["onboarding.key_unknown", "onboarding.provider_unknown"], ignoreOrder: true);
    }

    [Fact]
    public void Max_rounds_defaults_to_three_and_must_be_a_whole_number_above_zero()
    {
        OnboardingConfigLoader.Parse("llm:\n  provider: anthropic\n  api_key: env:K\n", "onboarding.yaml", Defaults).Value().MaxRounds.ShouldBe(3);
        OnboardingConfigLoader.Parse("llm:\n  provider: anthropic\n  api_key: env:K\n  max_rounds: 5\n", "onboarding.yaml", Defaults).Value().MaxRounds.ShouldBe(5);
        OnboardingConfigLoader.Parse("llm:\n  provider: anthropic\n  api_key: env:K\n  max_rounds: 0\n", "onboarding.yaml", Defaults).Problems()
            .ShouldHaveSingleItem().Code.ShouldBe("onboarding.rounds_invalid");
    }
}
