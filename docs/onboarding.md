# Onboarding with a language model

`hound onboard` helps a GRC team member onboard a system: a language model the person chooses proposes how the system fits a framework and
drafts its configuration, and the person accepts or edits every decision. The output is files for a pull request in the config repo; nothing
is applied to CISO Assistant until a person reviews it and runs `hound scope validate`, `plan` and `apply`. The design is in
[design/onboarding-assistant.md](design/onboarding-assistant.md).

**No model in evidence collection.** Only `hound onboard` uses a model. `hound run`, `hound serve` and every collector never do: they cannot
reach the onboarding code, and the runner refuses a deployment config (`hound.yaml`) that has an `llm` section or refers to the onboarding
key (`config.llm_in_runner`).

## Set up the model

```console
$ hound onboard config
Model provider (anthropic or openai) [anthropic]:
Model (empty for the provider's default):
API key for anthropic (not shown):
Wrote /work/config-repo/onboarding.yaml; the key is in /home/example/.config/hound/secrets/llm_api_key, readable only by you.
$ hound onboard config --check
anthropic claude-opus-5-5: the key works.
```

The key is saved once, outside the repo, in a file only you can read (`$XDG_CONFIG_HOME/hound/secrets/llm_api_key`, else
`~/.config/hound/secrets/llm_api_key`). `onboarding.yaml` holds only a reference to it, so it is safe to commit. `--check` makes one minimal
request and never prints the key. Options: `--provider`, `--model`, `--config <file>` and `--yes`.

## onboarding.yaml

Separate from `hound.yaml`; only `hound onboard` reads it.

| Key | Meaning |
| --- | --- |
| `llm.provider` | Required. `anthropic` or `openai`. |
| `llm.model` | The model. Empty means the provider's default from `onboarding/models.yaml`, shipped beside `hound` and editable without a new build (`claude-opus-5-5` and `gpt-6.2-astra` today). |
| `llm.api_key` | Required. A reference, `env:NAME` or `file:/path`. A key written here is refused (`onboarding.api_key_literal`). |
| `llm.effort` | Optional: `low`, `medium`, `high` or `max`. How hard the model thinks. |
| `llm.max_tokens_per_session` | Optional, default 2,000,000. A session stops asking the model once it has used this many tokens (`llm.budget_reached`). |
| `llm.deny_terms_file` | Optional. A text file of terms, one per line, that must never be sent: your organisation's name, domains, project names. |
| `skills_path` | The organisation's own skills. Default `/config/skills`. |
| `profiles_path` | The organisation's profiles folder. Default `/config/profiles`. |

## What is sent, and what is not

Every request passes one gate before it leaves:

- **The outbound guard** scans the instructions, every input and the schema for the values of the secrets in use, the deny terms, and email,
  IP and MAC addresses. A hit sends nothing and names what matched and where (`llm.blocked_content`), never the matched value. Documentation
  addresses pass (`192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24`, `2001:db8::/32`, loopback, `example.com`), so fictional material can be
  sent.
- **Response shapes, not responses.** A system's API answers are reduced to field names, types and counts. Values are kept only for
  settings-like fields (a few short distinct values, such as `active` and `inactive`) and never for a profile's `identities`, email
  addresses, IDs or free text.
- **A local record.** Every request and reply is saved with the session (`NNN-step.request.json`, `NNN-step.reply.json`), and
  `--show-requests` prints each request before it is sent.
- **Replies are checked.** Each reply must meet the step's JSON schema (`llm.invalid_reply`); a model that declines is `llm.refused`, and a
  provider that cannot be reached is `llm.unreachable`.

## Skills: the assistant's guidance

A skill is a Markdown file with frontmatter that tells the model how to approach a step. Skills only guide: the outbound guard, response
shapes, the named approver for not applicable, the supplier gate, schema checks and profile tests stay in code, and no skill can change them.

```markdown
---
name: our-applicability
description: How we justify not applicable.
version: 1.0.0
applies_to: [applicability]          # supplier, system, applicability, profile, mapping
frameworks: [essential-eight-2023]    # optional: only for these frameworks
overrides: applicability              # optional: replace this default on purpose
---
Guidance for the model, in plain Markdown.
```

- **Defaults** are built into `hound`, from this repository's [`skills/`](../skills/) folder: supplier assessment and tiering, supplier
  assurance documents, applicability and not-applicable justification, Essential Eight ML2 mapping, profile authoring, and reading an API
  specification.
- **Organisation skills** live in the config repo's `skills/` folder (`skills_path`, mounted read-only like profiles). They add to the
  defaults; one with a default's name is an error unless it says `overrides:`.
- **Choosing:** at each step the model gets every skill whose `applies_to` names the step and whose `frameworks` match, defaults first, within
  a size budget. Over the budget is an error naming the skills, never a silent cut. Specifications and responses are passed separately, as
  material to read.
- **Recorded:** each session's `skills.json` lists every skill used with its source, version and SHA-256.

| Command | Does |
| --- | --- |
| `hound skill list` | Every active skill: source, version, SHA-256, steps and override. |
| `hound skill new <name>` | Writes a skeleton to fill in. |
| `hound skill add <file>` | Validates the skill against the defaults and the folder, then copies it in. |
| `hound skill validate [--deny-terms <file>]` | Checks every skill and warns about anything that looks like organisation data, since skills are sent to the model provider. |

Each takes `--skills <folder>` (default `HOUND_SKILLS_PATH`, else `./skills`).
