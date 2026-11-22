# Design: an assistant for onboarding a system

Status: Proposed (Nov 22). Builds on the agreed [REST collector design](rest-collector.md). The build is tasks s09-19 to s09-23 in [the s09 sprint](../sprints/s09-e8-gap-collectors.md).

## The problem

The REST collector turns a new system into configuration instead of code, but someone still has to write that configuration. To onboard a system today they need to understand three things at once:

- **The system:** what its API exposes, how it signs in and pages, which fields matter.
- **The standard:** what each requirement of Essential Eight ML2 (or, later, NIST SP 800-171) actually asks for.
- **The fit between them:** which requirements this system can evidence through its API, which need manual evidence, and which do not apply, with a reason an auditor will accept.

That is slow, specialist work, and it is the same work for every one of the roughly 13 systems. A language model is good at reading API documentation and requirement text and proposing a fit. It is not trustworthy enough to decide compliance on its own.

## Goals

1. `hound` helps a person onboard a system: understand it, choose what applies from the target standard, and draft the profile, mapping and not-applicable justifications.
2. **Everything the model produces is a draft.** A person reviews every decision before it is saved, and nothing reaches CISO Assistant until it is committed and applied through the normal `hound scope plan` and `apply` flow.
3. **No organisation data leaves without the person seeing it.** API documentation and requirement text go to the model; secrets, tenant identifiers, host names, addresses and people never do.
4. A drafted profile is held to the same bar as a hand-written one: it must pass `hound profile validate` and `hound profile test` before it is offered.
5. The model provider is swappable, and the model is a setting.
6. Framework-neutral: the same system can be onboarded again for another standard, adding only mappings and applicability.

Not goals: an agent that runs unattended, changes CISO Assistant directly, or decides applicability without a person.

## How it works: `hound onboard`

`hound onboard <system> --framework <framework file>` is a guided session in the terminal, in the same style as the `hound add` wizards. It works in the organisation's config repo working copy and writes files there; the person commits them through a pull request.

```
1. Describe        2. Read the system      3. Decide applicability     4. Draft profile      5. Sample and test      6. Write files
person names       API spec or docs        per requirement:            queries, datasets,    one read-only run,      profile, tests,
system, owner,     (local files or         API check / manual /        checks for the        shapes to the model,    mappings, scope
product            allowed URLs)           not applicable + reason     "API check" rows      validate + test loop    assessments, record
                                           person accepts or edits
```

**1. Describe.** The person names the system, its owner and the product. If a library profile exists for the product (for example `sophos-central`), the session starts from a copy of it instead of from nothing.

**2. Read the system.** The person points `hound` at an OpenAPI or other API specification, or at saved documentation pages (local files, or URLs on hosts they allow). The model summarises what can be read: endpoints, sign-in method, paging, and fields that look relevant to security.

**3. Decide applicability.** `hound` reads every in-scope requirement from the framework file (the same files `hound scope new-audit` uses), and the model proposes one of four outcomes for each, with its reasoning:

| Outcome | Meaning | Where it lands |
| --- | --- | --- |
| API check | The API can evidence it | A check in the profile and a row in the framework's mapping file |
| Manual | It applies, but the API cannot show it | A manual source with instructions for the system owner |
| Not applicable | It does not apply to this system | `result: not_applicable` with the justification as the observation, in the system's audit scope file |
| Not this system | Another system evidences it | Nothing for this system; noted in the onboarding record |

The person goes through the list and accepts, edits or rejects each proposal. A not-applicable outcome cannot be saved without a justification and the name of the person who approved it.

**4. Draft the profile.** For each accepted API check, the model drafts the queries, datasets and checks, using framework-neutral check IDs. The output is structured (validated against the profile schema), never free-form YAML pasted into a file.

**5. Sample and test.** With the person's go-ahead, `hound` runs the drafted queries once against the real system, read-only, using the credentials in their config. The responses are saved as test recordings with identities hashed. The model then sees only the **shape** of each response (see "What the model sees"), fixes paths that did not match, and proposes expected results. `hound profile validate` and `hound profile test` run after each round; their problems go back to the model, for a set number of rounds. The person confirms each expected result before it is saved as a test.

**6. Write files.** Into the working copy:

- `profiles/<system>.yaml` and `profiles/<system>.tests/` (recordings and expected results);
- additions to `mappings/<framework>.yaml`, shown as a diff;
- not-applicable and manual rows in the system's audit scope file (`scope/audits/<audit>.yaml`), each with its justification and approver;
- manual source stubs for manual rows, ready for the evidence plan;
- `onboarding/<system>-<date>.md`: the onboarding record, covering the system summary, every decision with who accepted it, the model and provider used, and what was sent.

Nothing is applied. The person reviews the diff, commits, and runs `hound scope plan` and `hound plan` as for any other change. The onboarding record can be attached to the audit in CISO Assistant as a document, so an auditor can see how applicability was decided.

## How not-applicable lands in CISO Assistant

Natively, through the scope files that already exist. A not-applicable decision becomes an `assessments` entry in the system's audit file:

```yaml
assessments:
  - ref: ML2-RA-05                # fictional reference
    status: done
    result: not_applicable
    observation: >-
      Not applicable: the system has no local user accounts; all access is through the identity provider.
      Approved by the system owner on 2026-11-22 (onboarding record onboarding/example-firewall-2026-11-22.md).
```

`hound scope apply` writes it to the requirement assessment's result and observation, as it does today. No new CISO Assistant feature or custom field is needed, and the justification sits on the requirement where an auditor looks for it.

## What the model sees

| Sent | Never sent |
| --- | --- |
| API specifications and documentation the person supplied | Secrets, tokens, client IDs, tenant IDs |
| Requirement text from the framework file | Host names, IP and MAC addresses, serial numbers, people, email addresses |
| The draft profile and validation problems | Raw API responses |
| **Shapes** of sampled responses: field names, types, counts, and values only for fields that look like settings (short enumerations and version strings), never identities | Anything in the organisation's deny-list |

Guard rails:

- **Shapes, not data.** Responses are reduced to their structure before the model sees them. Values are kept only for fields whose distinct values are few and match no identity pattern, and identity fields named in the profile are always dropped.
- **A final check before every request.** The outgoing request is scanned for every secret value in use, the organisation's deny-list terms (setting `llm.deny_terms`, the same idea as the repo's `ORG_DENYLIST`), and patterns for addresses and email. A hit stops the request and tells the person what matched.
- **Everything sent is kept.** Each request and reply is saved locally under the onboarding record, so the person can see exactly what left. `--show-requests` prints each one before it is sent.
- **The key is a secret like any other.** `llm_api_key` comes from the secret provider (environment or secret file), never from a config file or CISO Assistant.

## Model and provider

- A small port in the core, `IDraftingModel`: send a request with instructions, inputs and the JSON schema the reply must match; get a validated reply or a problem. The onboarding logic depends only on this.
- The first adapter uses the Anthropic API through the official .NET SDK. The default model is `claude-opus-5-5`, with adaptive thinking and structured outputs. Model, effort and a token budget per session are settings (`llm.provider`, `llm.model`, `llm.effort`, `llm.max_tokens_per_session`).
- Other providers (a cloud-hosted equivalent, or a local model for organisations that cannot send anything out) are further adapters behind the same port.
- A refusal, an invalid reply or a budget reached is a problem returned to the session, which tells the person and keeps whatever was already accepted.
- Tests use a fake model with recorded replies. No test or CI job calls a live model.

## Trade-offs

| | We gain | It costs |
| --- | --- | --- |
| A model drafts, a person decides | Onboarding goes from days of reading to a reviewed session | Review is still real work; a careless "accept all" would be a bad audit trail, so not-applicable rows need a named approver |
| Shapes, not data, to the model | No organisation data leaves | The model works from less context, so path fixes may take another round |
| Live sample run | Paths are proven against the real system before saving | Needs working read-only credentials during onboarding |
| Structured replies validated against the schema | No hand-fixing of malformed YAML | More code in the engine to define the schemas |
| Swappable provider | No lock-in; a local model is possible | One adapter to maintain per provider |
| Onboarding record kept as evidence | Auditors see how applicability was decided | Another document to keep current when a system changes |

## Edge cases

| Case | How it is handled |
| --- | --- |
| No API specification exists | The person saves documentation pages; the model works from those, and the sample run matters more. |
| The API is XML (for example an on-device firewall API) | The engine already converts XML to the same tree, so the drafting is the same. |
| A requirement needs data from two systems | Proposed as "not this system" or "manual"; cross-system joins wait for a real case, as in the REST design. |
| Re-onboarding for another framework | Starts from the existing profile; adds checks only where the new framework needs them, plus mappings and applicability. |
| The system changes later | Run `hound onboard --update`: compares the current spec and a fresh sample with the profile and proposes changes, never applying them. |
| No model key, or the organisation forbids sending anything out | `hound onboard --no-model` walks the same steps with empty proposals, so the person fills them in. |
| The model is wrong about applicability | The person rejects it; the record keeps both the proposal and the decision. |

## Decisions for you

1. **A guided terminal session (`hound onboard`)**, writing files into the config repo for a pull request, rather than writing to CISO Assistant directly. (Recommended.)
2. **Shapes, not data:** the model never sees raw responses, only structure and settings-like values. (Recommended.)
3. **Not-applicable needs a named approver**, and lands as a scope-file assessment with the justification as its observation. (Recommended.)
4. **Anthropic first, behind a provider port**, default model `claude-opus-5-5`, with the model and provider as settings. (Recommended.)
5. **Your Sophos firewall is the first real onboarding** (s09-23), run by you in the dev container with your own key. (Recommended.)
