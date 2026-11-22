# s09 Essential Eight gap collectors

Status: In progress

**Goal:** close the largest Essential Eight ML2 gaps left after the Microsoft 365, Sophos and Veeam collectors: application control evidenced from Intune and Microsoft Defender for Endpoint, patching evidenced from imported vulnerability scans, and a ready-made manual-upload template for every requirement that stays manual. After this sprint every ML1 and ML2 row in the example Essential Eight audit gets a result from a check or an attested manual source.

New sprint, added in the release 1 reorder. Application control extends the Microsoft 365 collector (same app registration). Vulnerability scans are imported by a new `vuln-scan` collector in `src/Collectors/EvidenceHound.Collectors.VulnScan`, which reuses the manual collector's drop folder and attestation handling. Recorded, fictional responses and exports only (Example Pty Ltd, `example.com`).

The patch windows and scan intervals below are settings, not constants, so the checks stay framework-neutral. The example config sets them to the Essential Eight ML2 values, and the mapping file quotes the ISM control each one comes from.

### REST collector (s09-09 to s09-14)

These tasks come first in this sprint, before s09-01, so the Microsoft 365 application control checks and the Veeam tasks can be written as profile checks instead of new code. They build the generic `rest` collector agreed in [`docs/design/rest-collector.md`](../design/rest-collector.md): every query is called once per run, evidence datasets are extracted from one or more responses, and checks test dataset fields, giving pass, fail or unable to assess. Profiles are YAML loaded only from the organisation's profiles folder; this repo keeps a tested public library in `profiles/` to copy from. There are no runtime code plugins. Where the Veeam tasks (s09-07, s09-08) and s09-01/02 say "collector", read "profile in the library" once these tasks are done.

### s09-09 Profile model and validation
- [ ] Done

**Story:** As an operator, I want a profile checked before anything is called, so that a typo or a wrong reference fails clearly instead of producing misleading results.

**Acceptance criteria**
- Immutable records for a profile: `parameters`, `secrets`, `connection` (base URL, `allowed_hosts`, auth, rate limit), `bootstrap`, `queries`, `datasets`, `checks`, `requires`, `source` and `version`; parsed from YAML into the core once.
- Collector type `rest` with settings `profile` plus the profile's own parameters; secrets as for other collectors.
- Validation (pure) reports every problem at once with a code: unknown parameter or secret, missing required one, bad path syntax, a check `over` or dataset `from`/`join` naming nothing, a cycle in query dependencies, a code check name that is not built in, a check without `when_empty`, a request or auth URL outside `allowed_hosts`, `requires` newer than this `hound`.
- `hound profile validate <name>` and `hound plan` both run it; a profile that fails stops only its own instances, which report unable to assess.
- Profiles are read only from `profiles_path` (default `/config/profiles`), mounted read-only.

**Tests first**
- `ProfileParserTests.Reads_example_profile_into_records`
- `ProfileValidationTests.Check_over_unknown_dataset_is_reported`
- `ProfileValidationTests.Query_dependency_cycle_is_reported`
- `ProfileValidationTests.Host_outside_allowed_hosts_is_reported`
- `ProfileValidationTests.Check_without_when_empty_is_reported`
- `ProfileValidationTests.Reports_all_problems_not_just_the_first`

**Functional check**
- `hound profile validate example` passes for `profiles/example.yaml`, and a broken copy lists each problem with its code.

### s09-10 Query engine
- [ ] Done

**Story:** As an operator, I want every endpoint read once per run, with sign-in, paging and rate limits handled for me, so that a new system needs a profile, not code.

**Acceptance criteria**
- Auth: OAuth 2.0 client credentials (secret or certificate), API key (header or query), bearer token, basic, mutual TLS. Tokens refresh on expiry and are sent only to `allowed_hosts`; cross-host redirects are refused.
- Paging: next link in the body, page number with total, cursor, offset and limit, `Link` header; a per-query page cap.
- Rate limits: per-profile rate, `Retry-After` on 429 and 503, bounded retries.
- Queries run as a dependency graph: `bootstrap` variables first, `for_each` fan-out capped by `max_calls`, `post:` bodies, `async` jobs (start, poll with a timeout, download), `optional_on` status codes for licence-dependent endpoints.
- JSON, XML and CSV responses become the same tree (XML attributes as `@name`, text as `#text`, profile-listed paths always arrays).
- Item and byte caps per query, with `keep:` to hold only listed fields.
- Each query is saved as one evidence file (request without headers, page count, item count, items) with `identities` hashed unless `include_identities`. A final scan refuses to save any file containing a secret in use.
- A failed query is a result for that query, not a failed run: later phases see which queries failed and why.

**Tests first**
- `RestAuthTests.Client_credentials_token_is_sent_only_to_allowed_hosts`
- `RestPagingTests.Follows_next_link_until_absent` (and one test per paging style)
- `RestPagingTests.Stops_at_page_cap_and_reports_it`
- `RestRateLimitTests.Honours_retry_after_then_gives_up`
- `QueryGraphTests.Bootstrap_variables_feed_later_requests`
- `QueryGraphTests.For_each_is_capped_by_max_calls`
- `AsyncQueryTests.Polls_until_done_then_downloads`
- `XmlTreeTests.Repeated_and_single_elements_read_the_same`
- `QueryEvidenceTests.Identities_hashed_and_secrets_never_saved`
- Contract tests against WireMock with fictional recordings.

**Functional check**
- `hound run --only rest-example --dry-run` against the recorded example API lists every query with its page and item counts and its evidence file.

### s09-11 Datasets and declarative checks
- [ ] Done

**Story:** As a compliance owner, I want evidence from several API calls combined into one dataset and tested field by field, so that a control is judged on exactly the evidence it needs and a broken query never shows as a failure.

**Acceptance criteria**
- Datasets: `from`, `join` (on a key, one-to-many, `optional` for a left join), `where`, `fields`, `flatten`. Each dataset is saved as an evidence file listing the query files and SHA-256 hashes it came from, and how many rows did not match a required join.
- A check reads one dataset (or one query directly, for a one-to-one check) and has `where`, `each` and `require` (`all`, `any`, `none`, `count_at_most`, `count_at_least`, `ratio_at_least` with an optional partial band).
- Operators: `equals`, `not_equals`, `in`, `matches`, `any_matches`, `exists`, `version_in`, `version_at_least`, `within`, `older_than`, `at_least`, `at_most`, each with `missing:` and `negate`.
- Results: pass, fail, partial (ratio bands only), not applicable (empty and `when_empty: not_applicable`), and unable to assess when a query the check depends on failed, paging stopped early, a cap was hit, a field was missing (unless `missing:` says otherwise) or a value had the wrong type. The observation names the cause.
- Failure propagates only to what depends on the failed query; a dataset is never built from part of its inputs.
- User-facing text says "unable to assess" for `CheckOutcome.Error`; the mapping still turns it into `not_assessed`.
- Observations use `describe` templates with counts, list offending rows capped at 20, and cite the dataset file.

**Tests first**
- `DatasetTests.Join_attaches_matching_rows_and_counts_unmatched`
- `DatasetTests.Failed_input_query_fails_the_dataset_with_its_name`
- `CheckEvaluatorTests.All_over_empty_uses_when_empty`
- `CheckEvaluatorTests.Missing_field_is_unable_to_assess`
- `CheckEvaluatorTests.Missing_pass_treats_absent_field_as_pass`
- `CheckEvaluatorTests.Ratio_band_gives_partial`
- `CheckEvaluatorTests.Independent_checks_still_run_when_one_query_fails`
- Property tests: evaluation is deterministic for the same snapshot and clock; adding a failed query never turns a fail into a pass.

**Functional check**
- `hound run --only rest-example --dry-run` shows each check's result and dataset evidence; breaking one recorded query makes only its checks unable to assess.

### s09-12 Code checks, profiles folder and profile tests
- [ ] Done

**Story:** As a profile author, I want to test a profile against recorded responses before it is trusted, and to use a built-in code check when the operators are not enough.

**Acceptance criteria**
- A code-check library in `hound`: named, pure functions taking the datasets they name, parameters and the clock, returning a finding. A profile names them with `code:`; nothing is loaded at runtime.
- `hound profile test <name>` runs a profile offline against its recordings and expected results (`<profile>.tests/`), reporting each check's expected and actual result. `hound` warns when a profile in the folder has no tests; `require_profile_tests: true` makes it an error.
- `hound profile diff <name>` compares a copied profile with the library version named in its `source`.
- Each run's manifest records every profile's name, version, `source` and SHA-256.
- The public library `profiles/` starts with `example.yaml` (a fictional API) and its tests; CI runs every library profile's tests.

**Tests first**
- `CodeCheckRegistryTests.Unknown_name_is_a_validation_problem`
- `ProfileTestRunnerTests.Reports_expected_and_actual_per_check`
- `ProfileTestRunnerTests.Profile_without_tests_warns_or_fails_by_setting`
- `ProfileDiffTests.Shows_changes_from_library_version`
- `RunManifestTests.Records_profile_name_version_and_hash`

**Functional check**
- Copy `profiles/example.yaml` into a scratch profiles folder, run `hound profile test example`, change an expected result, and see the test fail naming the check.

### s09-13 Sophos as a profile
- [ ] Done

**Story:** As an operator, I want Sophos Central read through a profile, so that changes to Sophos checks are configuration, and as the proof that the REST collector can replace a coded one.

**Acceptance criteria**
- `profiles/sophos-central.yaml` with its tests: `whoami` bootstrap, firewalls, admins and roles, the `admin_accounts` and `firewall_firmware` datasets, and every check the coded collector reports (including any added by s08-12 to s08-14).
- A parity test runs the coded collector and the profile on the same recordings and requires the same check IDs, outcomes and evidence content.
- When parity passes, the coded Sophos collector is removed. A `type: sophos` instance fails validation with a message showing the `type: rest`, `profile: sophos-central` replacement; the example config and `docs/collectors/sophos.md` are updated.
- The interactive tutorial (`make trial`) copies the profile into the trial's profiles folder and uses it.

**Tests first**
- `SophosProfileParityTests.Profile_matches_coded_collector_on_every_recording`
- `SophosProfileTests` via `hound profile test sophos-central`
- `CollectorConfigTests.Sophos_type_suggests_rest_profile`

**Functional check**
- `hound run --only sophos-example --dry-run` with the profile gives the same results as before; the trial's Sophos step passes with the profile.

### s09-14 REST collector documentation
- [ ] Done

**Acceptance criteria**
- `docs/collectors/rest.md`: profile reference (connection, auth, paging, queries, datasets, checks, operators, results, limits), the profiles folder, the library and how to copy from it, and security notes (`allowed_hosts`, secrets, identities).
- `docs/tutorials/write-a-profile.md`: write a profile for the fictional example API test-first, from recording to green `hound profile test`.
- `docs/design/rest-collector.md` status set to Implemented, with any changes made during the build.

**Tests first**
- Docs check that every operator and result in the engine appears in `docs/collectors/rest.md`.
- Docs lint and link check.

**Functional check**
- Following the tutorial verbatim produces a passing profile.

### Suppliers before systems (s09-19, s09-20, moved from s10)

A system is registered only once its supplier is approved, so the supplier register comes before onboarding. s10-01 and s10-02 moved here unchanged except for the supplier approval gate; s10-03 (documentation and example repo) and s10-04 (evidence plan tasks, including the recurring supplier review task) stay in s10.

### s09-19 Suppliers, solutions and contracts (was s10-01)
- [ ] Done

**Story:** As a supplier owner, I want our suppliers, what they provide and our contracts with them declared in the repo, so that the supplier register is reviewed and complete.

**Acceptance criteria**
- `scope/third-parties/<entity>.yaml` declares one entity: `ref_id`, name, description, mission, country (ISO 3166-1 alpha-2), currency (ISO 4217), parent entity, domain, and its dependency (0–4), penetration (0–4), maturity (1–4) and trust (1–4).
- It also declares the entity's solutions (`ref_id`, name, description, criticality 1–4) and contracts (`ref_id`, name, status `draft`, `active`, `expired` or `terminated`, start and end dates, annual expense, currency, the solutions covered, and identifiers such as LEI or VAT).
- Representatives are optional. They are personal data, so the guide recommends keeping them in CISO Assistant unless the repo is private and access-controlled; when declared, they are matched by email and never given a login by `hound`.
- If s06-05 confirmed the API can update the main entity, `organisation` in `scope/setup.yaml` manages it here (the s06 discovered-work item).
- Validation: country and currency codes; contract end after start; an `active` contract past its end date is reported (`supplier.contract_lapsed`); solutions named by a contract belong to the same entity.

**Tests first**
- `SupplierScopeTests.Invalid_country_or_currency_is_reported`
- `SupplierScopeTests.Active_contract_past_end_date_is_reported`
- `SupplierScopeTests.Contract_solution_must_belong_to_entity`
- Contract: `SupplierClientTests.Creates_entity_solution_and_contract`

**Functional check**
- Apply two fictional suppliers to the local CISO Assistant; re-applying writes nothing; a changed contract end date is a one-field update.

### s09-20 Supplier assessments, tiers and approval (was s10-02)
- [ ] Done

**Story:** As a supplier owner, I want each supplier's assessment set up from the repo on a review cycle, so that reviews start on time and nobody sets them up by hand.

**Acceptance criteria**
- An entity file may declare assessments: name, solutions, perimeter, due date, criticality, the questionnaire framework (a library URN), and `review_every` (such as `12 months`). Applying creates the entity assessment and its linked questionnaire audit.
- When a review is due, `hound` opens the next one: a new revision if the API supports it (s06-05), otherwise a new assessment named with its period, using the same periods logic as audits (s06-10).
- Answers, tiering form results and campaigns are never written by `hound`.
- `forced_tier` (one of the criticality scale's keys, such as `critical`) with a required `reason` may be declared; `scope plan` reports when CISO Assistant's current tier differs from it.
- Approval: a supplier is approved when its latest entity assessment is concluded as acceptable in CISO Assistant (its `conclusion` field; confirm the field and its values against the pinned release's schema in the first test) and is within its review cycle, and it has an `active` contract. Approval is read from CISO Assistant, never written by `hound`.
- Supplier gate: a system in `scope/` names its `solution`, and optionally `supplied_through` (the reseller or provider the contract is with). `scope plan` refuses to register the system, or add it to an audit, until every named supplier is approved (`system.supplier_not_approved`, naming the supplier and what is missing). A supplier whose approval later lapses (review overdue, conclusion changed, contract ended) does not unregister the system; `scope plan` reports `supplier.approval_lapsed` and the system is listed for the committee.
- The recurring review task for `review_every` comes with s10-04 (evidence plan tasks), which stays in s10.

**Tests first**
- `SupplierAssessmentTests.Due_review_opens_the_next_one`
- `SupplierAssessmentTests.Answers_are_never_in_the_plan`
- `SupplierScopeTests.Forced_tier_requires_reason`
- `ScopePlanTests.Tier_differing_from_forced_tier_is_reported`
- `SupplierGateTests.System_with_unapproved_supplier_is_not_registered`
- `SupplierGateTests.Reseller_named_by_supplied_through_must_also_be_approved`
- `SupplierGateTests.Lapsed_approval_is_reported_not_unregistered`

**Functional check**
- With a fixed clock past a review date, `scope apply` opens the next fictional supplier assessment in the local CISO Assistant with its questionnaire.
- A fictional system whose supplier has no concluded assessment is refused by `scope plan`; concluding the assessment in CISO Assistant lets it through.

### Onboarding assistant (s09-21 to s09-26)

These tasks come after s09-14, because they draft REST profiles, and after s09-20, because onboarding starts with the supplier. They build `hound onboard` from [`docs/design/onboarding-assistant.md`](../design/onboarding-assistant.md): a guided session in which a language model proposes how a system fits a framework and drafts its profile, and a person accepts or edits every decision. Nothing is applied to CISO Assistant by the session; the output is files for a pull request in the organisation's config repo. Tests use a fake model with recorded replies; no test or CI job calls a live model.

### s09-21 Model port, Anthropic and OpenAI adapters, onboarding config and outbound guard
- [ ] Done

**Story:** As an operator, I want `hound` to use a language model I choose, with my key kept as a secret and nothing sensitive sent, so that I can use the assistant without leaking organisation data.

**Acceptance criteria**
- Core port `IDraftingModel`: instructions, inputs and a JSON schema in; a reply validated against the schema, or a problem (`llm.refused`, `llm.invalid_reply`, `llm.budget_reached`, `llm.unreachable`), out.
- Two adapters, each on its provider's official .NET SDK: Anthropic (structured outputs, adaptive thinking, streaming for long replies) and OpenAI (structured outputs with a JSON schema). Both pass one shared adapter contract suite.
- `onboarding.yaml`, separate from the deployment config: `llm.provider` (`anthropic` or `openai`, required), `llm.model` (empty means the provider's default), `llm.api_key` (a secret reference, `env:` or `file:`; a literal key fails validation), `llm.effort`, `llm.max_tokens_per_session`, `llm.deny_terms_file`, `skills_path`, `profiles_path`.
- Default models per provider in `onboarding/models.yaml`, shipped with `hound` and editable without a build: each provider's current top model (`claude-opus-5-5` for Anthropic; OpenAI's confirmed from its model list when this task is built).
- `hound onboard config`: asks for provider and model, reads the key without echo, saves it to `~/.config/hound/secrets/llm_api_key` with owner-only permissions, and writes `onboarding.yaml` with a `file:` reference. `--check` resolves the key and makes one minimal request to confirm it works, never printing the key.
- Outbound guard (pure): every request is scanned for secret values in use, the deny terms, and IP, MAC and email patterns; a hit returns `llm.blocked_content` naming what matched and nothing is sent.
- Response shapes (pure): a response reduced to field names, types and counts, keeping values only for settings-like fields (few distinct values, no identity pattern) and never for profile `identities`.
- Every request and reply is saved locally with the session; `--show-requests` prints each before it is sent.
- No model in evidence collection: the onboarding code and model adapters live in `EvidenceHound.Onboarding`, used only by `hound onboard`. An architecture test fails if the core, runtime, collectors or `hound run` reference it or any model SDK. `hound run` and the scheduler refuse a deployment config that declares an `llm` section or references the onboarding key (`config.llm_in_runner`).

**Tests first**
- `ArchitectureTests.Runner_and_collectors_do_not_reference_onboarding_or_model_sdks`
- `RunnerConfigTests.Llm_key_or_setting_in_runner_config_is_refused`
- `OutboundGuardTests.Secret_value_in_request_blocks_it`
- `OutboundGuardTests.Deny_term_and_email_are_reported`
- `ResponseShapeTests.Keeps_enumerations_drops_identities_and_free_text`
- `DraftingModelContractTests<T>` run for both adapters against recorded exchanges (WireMock): sends the schema and configured model, maps refusals and invalid replies to problems
- `OnboardingConfigTests.Literal_api_key_is_refused`
- `OnboardingConfigTests.Empty_model_uses_provider_default`
- `OnboardConfigCommandTests.Writes_reference_and_owner_only_key_file`

**Functional check**
- With a fake model, `hound onboard --show-requests` on the example system prints requests containing no recorded host names, IDs or secrets.

### s09-26 Onboarding skills
- [ ] Done

Built straight after s09-21, before s09-22, because every later onboarding step reads its guidance from skills.

**Story:** As a GRC team member, I want the assistant's guidance in text files I can read and adjust without a build, with good-practice defaults and a way to add our own, so that onboarding improves as we learn.

**Acceptance criteria**
- A skill is a Markdown file with YAML frontmatter: `name`, `description`, `version`, `applies_to` (one or more of `supplier`, `system`, `applicability`, `profile`, `mapping`), optional `frameworks` and optional `overrides`. Parsed and validated once into an immutable record.
- Default skills in `skills/`, built into `hound`: supplier assessment and tiering, supplier assurance documents, applicability and not-applicable justification, Essential Eight ML2 mapping, profile authoring, reading an API specification.
- Organisation skills load from `skills_path` (default `/config/skills`, read-only). They add to the defaults; a skill with the name of a default is an error unless it declares `overrides:` with that name.
- `hound skill list` (source, version, SHA-256, overrides), `hound skill new <name>`, `hound skill add <file>` (validate, then copy into the organisation folder) and `hound skill validate` (frontmatter, step names, size budget, and a warning for anything matching the deny terms or identity patterns).
- Selection (pure): for a step and framework, every matching skill, defaults first, within a size budget; over budget is a problem naming the skills, not a silent cut.
- Skills are passed to the model as instructions; vendor documents and specifications are passed separately as material to read. The outbound guard scans skills like any other content.
- The onboarding record lists every skill used with its source, version and SHA-256.
- Skills cannot change the outbound guard, response shapes, the approver rule, the supplier gate, schema validation or profile tests; a test shows a skill telling the model to skip approval or send raw data has no effect on what is sent or saved.

**Tests first**
- `SkillParserTests.Reads_frontmatter_and_body`
- `SkillValidationTests.Unknown_step_and_missing_fields_are_reported`
- `SkillCatalogueTests.Same_name_without_overrides_is_an_error`
- `SkillCatalogueTests.Override_replaces_default_and_is_listed`
- `SkillSelectionTests.Selects_by_step_and_framework_within_budget`
- `SkillSafetyTests.Skill_cannot_weaken_guard_or_approver_rule`
- `OnboardRecordTests.Lists_skills_with_version_and_hash`

**Functional check**
- `hound skill new example-check`, edit it, `hound skill add` it, and `hound skill list` shows it beside the defaults; an onboarding run with a fake model records it.

### s09-22 Understand the system and decide applicability
- [ ] Done

**Story:** As a GRC team member, I want `hound` to read a system's API documentation and the target framework and propose, for each requirement, whether it is an API check, manual, not applicable or for another system, so that I decide applicability quickly and with reasons.

**Acceptance criteria**
- `hound onboard <system> --framework <framework file>` asks for the system name, owner and product, and starts from a copy of a library profile when one names the product.
- Supplier first: the session asks who provides the system and who the contract is with. For a supplier not yet in the repo it drafts the entity, solution and contract files (s09-19) and a supplier assessment (s09-20), with proposed dependency, penetration, maturity, trust and criticality and the reasons for each, and a supplier summary drawn from the vendor's public documents (certifications, where data is held, support lifecycle), each with its source. The person accepts or edits each. The summary is a draft kept in the onboarding record, not evidence: the vendor documents the person keeps are downloaded with source URL, retrieval date and SHA-256 and attached to the supplier assessment as its evidence. Questionnaire answers are never drafted into CISO Assistant.
- If a named supplier is not yet approved, the session saves, writes the supplier files and stops before applicability; it resumes from there once the supplier is approved.
- Reads an OpenAPI document, other API specifications or saved documentation from local files or allowed URLs, and shows the model's summary of readable endpoints, sign-in and paging.
- Reads every in-scope requirement from the framework file and shows one proposal per requirement (API check, manual, not applicable, not this system) with its reasoning; the person accepts, edits or rejects each, and can save and resume.
- Not applicable cannot be accepted without a justification and an approver name.
- `--no-model` runs the same steps with empty proposals.

**Tests first**
- `OnboardSupplierTests.Unapproved_supplier_stops_before_applicability_and_resumes_after`
- `OnboardSupplierTests.Supplier_proposals_carry_reasons_and_sources`
- `OnboardSupplierTests.Summary_is_not_evidence_and_kept_documents_are_attached_with_hash`
- `ApplicabilityTests.Not_applicable_without_justification_or_approver_is_refused`
- `ApplicabilityTests.Every_in_scope_requirement_gets_one_decision`
- `OnboardSessionTests.Resume_keeps_accepted_decisions`
- `SpecReaderTests.Reads_openapi_paths_and_security_schemes`

**Functional check**
- Onboard the fictional example API against the example framework with a fake model; the decision list covers every requirement and resumes after a stop.

### s09-23 Draft, sample and test the profile
- [ ] Done

**Story:** As a GRC team member, I want the profile for the accepted API checks drafted, proven against the real system and tested, so that I only review a profile that already works.

**Acceptance criteria**
- The model drafts queries, datasets and checks for every accepted API check, with framework-neutral check IDs, as a structured reply validated against the profile schema.
- With the person's go-ahead, the drafted queries run once, read-only, with the configured credentials; responses are saved as test recordings with identities hashed, and only their shapes go to the model.
- `hound profile validate` and `hound profile test` run after each round; their problems go back to the model, for at most `llm.max_rounds` (default 3) rounds, and the remaining problems are shown.
- The person confirms each check's expected result before it is saved as a test.

**Tests first**
- `ProfileDraftTests.Reply_not_matching_schema_is_a_problem`
- `DraftLoopTests.Validation_problems_are_fed_back_until_clean_or_rounds_used`
- `SampleRunTests.Recordings_are_hashed_and_only_shapes_are_sent`

**Functional check**
- Against the recorded example API, a fake model's first draft with a wrong path is fixed in the second round, and `hound profile test` passes.

### s09-24 Write the onboarding output
- [ ] Done

**Story:** As a GRC team member, I want the session to write the profile, mappings, applicability and a record of how it was decided into my config repo, so that I can review it in a pull request and show auditors how applicability was decided.

**Acceptance criteria**
- Writes `profiles/<system>.yaml` and its tests, mapping additions to `mappings/<framework>.yaml` (shown as a diff), scope assessments for not-applicable rows (`result: not_applicable`, justification and approver in the observation) and manual rows, and manual source stubs.
- Writes `onboarding/<system>-<date>.md` with the system summary, every decision with its proposal and who accepted it, the provider and model, and links to the saved requests.
- Writes only into the working copy; never calls CISO Assistant. `hound scope plan` and `hound plan` then show the changes as for any other edit.
- Re-running for another framework keeps the profile and adds only new checks, mappings and applicability; `--update` proposes profile changes from a fresh spec and sample, never applying them.

**Tests first**
- `OnboardOutputTests.Not_applicable_becomes_scope_assessment_with_justification`
- `OnboardOutputTests.Mapping_additions_do_not_change_existing_rows`
- `OnboardOutputTests.Record_lists_every_decision_and_model`
- `OnboardOutputTests.Second_framework_adds_without_rewriting_profile`

**Functional check**
- After onboarding the example system, `hound scope plan` shows the not-applicable rows with their justifications and `hound plan --dry-run` shows the new checks.

### s09-25 Onboard the Sophos firewall, and documentation
- [ ] Done

**Story:** As the person running this project, I want to onboard my own Sophos firewall with `hound onboard`, so that the assistant is proven on a real system and real requirements.

**Acceptance criteria**
- The person runs `hound onboard` in the dev container with their own model key and read-only firewall credentials, registers and approves the supplier (the vendor or the reseller their contract is with) first, against the Essential Eight ML2 framework, and commits the output to their private config repo. Nothing from the run is committed here.
- What the run showed (steps that were slow or confusing, wrong proposals, missing operators) is recorded as discovered work, without organisation data.
- `docs/onboarding.md`: how to onboard a system, what is sent to the model and how to check it, how to use another provider or `--no-model`, how to write, add and override skills, and how auditors read the onboarding record. A tutorial section onboards the fictional example API.

**Tests first**
- Docs check that every `hound onboard` option appears in `docs/onboarding.md`.
- Docs lint and link check.

**Functional check**
- The person's onboarding run produces a profile that passes `hound profile test`, and the firewall's checks appear in a `hound plan --dry-run`.

### s09-01 Application control policies from Intune
- [ ] Done

**Story:** As a compliance owner, I want application control policy evidenced from Intune, so that the application control strategy has live evidence instead of screenshots.

**Acceptance criteria**
- The M365 collector reads App Control for Business policies (Intune endpoint security, settings catalog) and custom profiles that configure the AppLocker CSP, with their assignments. `DeviceManagementConfiguration.Read.All` is already granted, so no new permission is needed.
- `endpoint.application_control.enforced`: `pass` when an enforced policy is assigned to all devices or to every group in `application_control_groups`; `partial` when a policy is in audit mode or covers only some of those groups; `fail` when none exists.
- `endpoint.application_control.file_types_covered`: `pass` when the enforced policy covers executables, software libraries, scripts and installers (App Control for Business covers them by design; AppLocker needs the Exe, Dll, Script and Msi rule collections enforced); `fail` naming the missing types.
- Policies set by Group Policy are not seen; the check says so in its detail and the docs list it as a limit.

**Tests first**
- `AppControlChecksTests.Enforced_policy_assigned_to_all_devices_passes`
- `AppControlChecksTests.Audit_mode_policy_is_partial`
- `AppControlChecksTests.Applocker_without_dll_rules_names_missing_type`
- `AppControlChecksTests.No_policy_fails`
- Contract kit run for the M365 collector with the new recordings.

**Functional check**
- `hound run --only m365-example --dry-run` against recordings shows both checks with the expected outcomes.

**Notes**
- Confirm the settings catalog template and setting IDs against a recorded response in the first test, and record them in the docs.

### s09-02 Application control events from Defender for Endpoint
- [ ] Done

**Story:** As a compliance owner, I want proof that allowed and blocked application control events reach a central log, so that the logging requirement is evidenced and blocked files are visible to the committee.

**Acceptance criteria**
- The M365 collector runs two advanced hunting queries through Microsoft Graph (`v1.0/security/runHuntingQuery`, application permission `ThreatHunting.Read.All`): App Control and AppLocker events from `DeviceEvents` over `application_control_window_days`, and onboarding state from `DeviceInfo`.
- `endpoint.application_control.events_centrally_logged`: `pass` when Defender for Endpoint is onboarded on at least `defender_onboarded_percent` of Intune-managed Windows devices and events were received in the window; `partial` when onboarding is below the threshold; `fail` when no events were received.
- Evidence: event counts per device and action type, and the most-blocked files with their publisher. No file content or user names beyond the account name in the event.
- Without a licence that includes advanced hunting the query is refused; the check reports `not_applicable` with that reason and the docs point to the manual template from s09-05.

**Tests first**
- `HuntingQueryTests.Sends_query_and_timespan`
- `AppControlEventsTests.Events_and_onboarding_above_threshold_passes`
- `AppControlEventsTests.Low_onboarding_is_partial`
- `AppControlEventsTests.Missing_licence_is_not_applicable_with_reason`

**Functional check**
- `hound run --only m365-example --dry-run` against recordings shows the check and its evidence file.

**Notes**
- Confirm which licences include advanced hunting through Graph, and record it in `docs/collectors/m365.md`.

### s09-03 Vulnerability scan import
- [ ] Done

**Story:** As an operator, I want scanner exports dropped in a folder and read as structured findings, so that any scanner the organisation already uses can feed patching evidence.

**Acceptance criteria**
- Collector type `vuln-scan` with settings `drop_folder`, `format` (`nessus` or `csv`), `scope` (one of `internet_facing`, `common_apps`, `other_apps`, `operating_systems`), `owner`, and for `csv` a column map (host, finding ID, CVE, severity, first seen, patch published, exploit available, unsupported, scan date).
- Nessus v2 (`.nessus`) and mapped CSV exports both parse to the same immutable `ScanFinding` and `ScanRun` records. A Defender vulnerability management CSV export works through the column map; the docs show the map.
- The newest export in the folder is the current scan; older exports are kept as evidence history. The same content dropped twice is read once (shared with the manual collector).
- Malformed exports fail the run with `vuln_scan.export_invalid` naming the file and line; nothing is published from a partial parse.

**Tests first**
- `NessusParserTests.Reads_hosts_findings_and_exploit_flag`
- `CsvScanParserTests.Column_map_reads_defender_export`
- `CsvScanParserTests.Missing_mapped_column_is_reported_with_line`
- `ScanFolderTests.Newest_export_is_current_scan`
- Contract kit run for `vuln-scan`.

**Functional check**
- Drop a fictional `.nessus` export with `hound manual upload --source vuln-example`; `hound run --only vuln-example --dry-run` lists the parsed scan.

### s09-04 Patching checks from scans
- [ ] Done

**Story:** As a compliance owner, I want scan frequency, patch timeliness and unsupported software measured from scans, so that the patch applications and patch operating systems strategies have evidence.

**Acceptance criteria**
- For each instance's scope, three checks: `vuln.<scope>.scanned_in_interval` (the current scan is newer than `scan_interval_days`), `vuln.<scope>.patched_in_window` (no open finding older than `patch_window_days`, or `exploited_patch_window_hours` when an exploit is available, counted from patch publication, else first seen), and `vuln.<scope>.unsupported_removed` (no finding marked unsupported).
- Each check's detail lists offending hosts and findings, capped at 20 with a count of the rest; the full list is in the evidence file.
- `hound collectors list` shows all twelve check IDs.
- Every new check is mapped in `mappings/essential-eight-2023.yaml`, and the requirements it covers are removed from `manual_only`.

**Tests first**
- `PatchChecksTests.Scan_older_than_interval_fails`
- `PatchChecksTests.Exploited_finding_uses_hours_window`
- `PatchChecksTests.Window_counts_from_patch_publication_when_known`
- `PatchChecksTests.Unsupported_software_fails_naming_hosts`
- `E8MappingTests.Every_reference_check_is_mapped` extended to the `vuln.*` checks.

**Functional check**
- `hound run` with fictional exports for two scopes updates the example Essential Eight audit's patching rows in the local CISO Assistant.

### s09-05 Manual-upload templates for the remaining requirements
- [ ] Done

**Story:** As a system owner, I want a ready template for each Essential Eight requirement that cannot be collected, so that I know what to export, how often, and how to attest it.

**Acceptance criteria**
- `examples/manual-sources/essential-eight/` holds one manual source per remaining area (at least user application hardening, administrative privilege reviews, privileged account restrictions, event log retention, and backup restore tests for organisations without Veeam). Each has a config snippet, a README (what to export, from where, how often, who attests) and the check IDs it supports. Each README is written so it can serve as the runbook an s10-04 evidence plan row points to.
- `hound manual template --source <name>` writes an attestation skeleton (`<file>.attest.yaml`) listing that source's check IDs with empty outcomes.
- Manual check IDs are mapped in `mappings/essential-eight-2023.yaml`, so their rows get attested results rather than `not_assessed`. Requirements still without a check stay under `manual_only` and name their template.

**Tests first**
- `ManualTemplateTests.Skeleton_lists_source_checks_with_empty_outcomes`
- `ExampleManualSourcesTests.Every_template_config_validates`
- `E8MappingTests.Every_manual_only_requirement_names_an_example_source`

**Functional check**
- Write a skeleton for one template, fill it in, upload a fictional export with it, and see the attested result in the example audit.

### s09-06 Documentation
- [ ] Done

**Acceptance criteria**
- `docs/collectors/m365.md` gains the application control checks, the `ThreatHunting.Read.All` permission with its justification, licence notes and limits.
- `docs/collectors/vuln-scan.md`: formats, column maps (including Defender's export), scopes, every check with its limits, and how to schedule exports from common scanners.
- `docs/collectors/manual-templates.md` indexes the templates by Essential Eight strategy.

**Tests first**
- Docs check-ID tests for the M365 and `vuln-scan` collectors.
- Docs lint and link check.

**Functional check**
- Following `docs/collectors/vuln-scan.md` verbatim, import a fictional export and see its checks.

Moved from s08 on 2026-11-22 at the person's request (s08 focuses on Sophos). Was s08-03 and s08-04.

### s09-07 Veeam access
- [ ] Done

**Story:** As the collector, I want Veeam REST API access with a read-only account, so that backup jobs and restore points can be read.

**Acceptance criteria**
- OAuth password grant against the VBR REST API, API version header configurable.
- TLS verification on by default; a pinned certificate thumbprint option for self-signed servers instead of disabling verification.
- Paged jobs, sessions and restore points.

**Tests first**
- `VeeamAuthTests.Sends_api_version_header`
- `VeeamTlsTests.Pinned_thumbprint_accepts_matching_cert`
- `VeeamTlsTests.Mismatched_thumbprint_fails`

**Functional check**
- Run against recordings: jobs and restore points in evidence.

### s09-08 Veeam checks and documentation
- [ ] Done

**Story:** As a compliance owner, I want backup success, retention and restore testing measured, so that backup requirements are evidenced.

**Acceptance criteria**
- `backup.jobs.success_last_24h`: every enabled job succeeded in its last run within 24 hours (window configurable).
- `backup.retention.meets_policy`: restore points span at least the configured retention days.
- `backup.immutable_or_offline_copy`: at least one copy is immutable or on an offline or rotated repository (configured repository names).
- `backup.restore_tested`: a restore test session (SureBackup or instant recovery) within the configured interval; otherwise `Fail`, with the manual collector as the documented fallback.

**Tests first**
- Outcome tests per check; contract kit.

**Functional check**
- Both collectors run together in `hound run` against recordings; status shows both healthy.

**Also (from s08-05):** `docs/collectors/veeam.md` as for M365, with the doc check-ID test; every Veeam check mapped in `mappings/essential-eight-2023.yaml` and removed from `manual_only` where it now covers a requirement; `veeam` added back to `E8MappingTests.ReferenceTypes`.

### Moved from s08

Added 2026-11-22 when s08 closed. The two Sophos tasks are built as checks in the `sophos-central` profile, so they come after s09-13 (Sophos as a profile) and are written in the profile, not in the coded collector. Each still starts by confirming what the Sophos Central API exposes; the Firewall API v1 and Switch API v1 specs could not be reached from the cloud build session, so the person saves them from developer.sophos.com for the build to read (not to commit).

### s09-15 Firewall firmware update timeliness (was s08-13)
- [ ] Done

**Story:** As a compliance owner, I want to know when a firewall has had a firmware update available for longer than allowed, so that ISM-1694 (internet-facing devices patched within two weeks) has live evidence, not only "still supported".

**Acceptance criteria**
- First, confirm in the Sophos Central Firewall API v1 spec how to read available firmware upgrades and when each was released. Known so far: `POST firewall/v1/firewalls/actions/firmware-upgrade-check` with `{"firewalls": [ids]}` returns each firewall's `upgradeToVersion` list and a `firmwareVersions` list. Still to confirm: whether any field gives a release date, and whether the Service Principal Read-Only role may call it (it is a POST that changes nothing). If release dates are not exposed, the check reports an available upgrade without age, and the task says what evidence still has to be manual.
- In the `sophos-central` profile: a `firmware_upgrades` query (fan-out per firewall, or one batched call), joined into the `firewall_firmware` dataset, and `network.firewall.firmware_current`: pass when no firewall has an upgrade outstanding for more than `firmware_update_days` (default 14); fail lists the counts, not names; no firewalls is `not_applicable`.
- Mapped to ISM-1694 for the perimeter firewall, and removed from `manual_only` only if it covers the requirement.

**Tests first**
- Profile tests from fictional recordings: up to date, upgrade within the window, upgrade overdue, no firewalls (`not_applicable`), and the check call refused (unable to assess, not fail).

**Functional check**
- Dry run against the person's tenant.

### s09-16 Sophos switches (was s08-14)
- [ ] Done

**Story:** As a compliance owner with Sophos switches, I want them read like firewalls, so that non-internet-facing network devices (ISM-1501, ISM-1695) have live evidence.

**Acceptance criteria**
- First, confirm from the Switch API v1 spec whether a read-only credential can list managed switches and their firmware. Known so far, from public client libraries only: `switch/v1` has `settings/mac-filtering` and `tasks`, with no switch list; the on-switch REST API is per device with its own credentials, so it is out of scope. If there is no list, close the task with that finding in `docs/collectors/sophos.md` and keep switches a manual source.
- If there is: a `switches` query and `network.switch.firmware_supported` against `supported_switch_firmware` in the profile; no switches is `not_applicable`.

**Tests first**
- Profile tests from fictional recordings, as for firewalls.

**Functional check**
- Dry run against the person's tenant, if it has switches.

### s09-17 Finish the Sophos trial on a real tenant (rest of s08-08)
- [ ] Done

**Story:** As the maintainer, I want the hands-on Sophos trial finished and written up, so that there is a record that Evidence Hound works end to end on a real tenant.

**Done in s08.** The person ran `make trial` through the Sophos dry run against their tenant; that run led to s08-11 (Sophos is read for network devices only) and the s08 discovered work.

**Acceptance criteria (what is left)**
- **(person)** A dry run with the s08-12 collector (version 2.1.0), confirming the Read-Only role can read `common/v1/admins` and `common/v1/roles` and that the predefined role is named Super Admin; outcomes recorded without names.
- **(person)** The real run, results on the mapped requirements in CISO Assistant with evidence attached, and a second `hound scope plan` that plans nothing.
- `docs/evidence/s08/s08-08.md` written from what the person reports: date, versions, the shape of the setup, each step's and check's outcome, compliance counts before and after, defects with links to their fixes. No identifiers.
- If s09-13 has landed by then, the run uses the `sophos-central` profile.

### s09-18 Microsoft 365 trial on a test tenant (rest of s08-10)
- [ ] Done

**Done in s08.** The trial's Microsoft 365 step, `hound add collector --secret-file`, and the tutorial section are built and tested (CI's unattended trial stops before Sophos and Microsoft 365).

**Acceptance criteria (what is left)**
- **(person)** Get a throwaway test tenant (Microsoft 365 Developer Program E5 sandbox if eligible, otherwise a Business Premium trial) and run `make trial` through the Microsoft 365 step.
- Record `docs/evidence/s08/s08-10.md`: date, versions, the kind of tenant, each step's and check's outcome, defects with links to their fixes. No tenant or client IDs, domains, user or device names.
- Fix any defect test-first; correct the tutorial wherever it was wrong or unclear.

## Discovered work

- Open items from s08's discovered work (trial token refresh, the seed's compliance owner, HTTP logging at `info`, wizard prompts, trial re-runs, evidence on applied controls, the "unlisted rule" wording, `HOUND_CA_URL` in the dev container) are listed in [s08](s08-sophos-veeam-collectors.md#discovered-work); pick them up here or in s12.

## Sprint review
