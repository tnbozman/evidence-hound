# Walkthrough: run the AI programme on the local stack

This walkthrough takes one new AI system from "someone wants to use it" to results in the AI6 self-assessment, on your own machine. It
uses the fictional Example Pty Ltd and a local CISO Assistant, so nothing you do reaches a real system. Allow 45 minutes. No AI
knowledge is needed: the [primer](../ai-grc-primer.md) explains the words, and [AI governance](../ai-grc.md) is the reference.

**The scene.** Example Pty Ltd's staff want a search assistant that answers questions from the company's own documents, including HR
policies and staff records. The IT team will set it up on a model they configure themselves. You are the GRC team member who registers
it, gets it screened and assessed, and shows the committee where the AI programme stands.

Each step starts with what it is for, then what to type. The dates below use 2026-11-24 as "today"; use your own today.

## 1. Start the local stack

**What it is for:** a CISO Assistant with Example Pty Ltd's scope already in it, including the AI6 framework, the AI6 audit
`EX-AI6-2026` and the two AI systems already registered.

From the dev container:

```bash
make ca-up
set -a && . .dev/ca.env && set +a      # HOUND_CA_URL and HOUND_CA_TOKEN for the commands below
hound() { dotnet run --project src/EvidenceHound.Cli -- "$@"; }   # hound from your checkout
```

Open CISO Assistant at `https://localhost:8443` and sign in as the admin user `make ca-up` printed.

Then work on a copy of the example compliance repo, so the example itself stays as it is:

```bash
mkdir -p .dev/ai-walkthrough && cp -R examples/compliance-repo .dev/ai-walkthrough/repo
```

## 2. Look at the register you already have

**What it is for:** knowing what is registered before you add to it, the way the committee sees it.

```bash
hound programme list --scope .dev/ai-walkthrough/repo/scope
```

The AI programme's line ends with `Register: 2 AI systems. screening: elevated 1, normal 1. use: built 1, customised 1.` In CISO Assistant, open
**Assets** and filter by the label `ai-system`: the same two systems, each with its screening outcome, use and oversight as labels and its register
entry at the end of its description.

## 3. Decide whether it is an AI system, and who supplies it

**What it is for:** only AI systems go in the register, and a supplier must be approved before their system is registered.

Open [the screening runbook](../../examples/compliance-repo/runbooks/ai-screening.md) (`runbooks/ai-screening.md` in the repo) and
follow sections 1 and 2. The search assistant produces answers from data with some autonomy, so it is an AI system. The IT team sets
it up on a model they configure, so its `use` is `customised`. Had it been a supplier's product, its `use` would be `third_party` and
it would name the supplier's approved solution as `solution` ([third parties](../third-parties.md)).

## 4. Answer the screening questions

**What it is for:** the answers set the screening outcome, and the outcome decides how much assurance the system needs.

Answer the National AI Centre's seven questions in section 3 of the runbook. For the search assistant:

| # | Question, in short | Answer |
| --- | --- | --- |
| 1 | Personal, sensitive or confidential information? | **Yes**: staff records |
| 2 | Talks to people without a person checking? | No: staff read each answer before acting on it |
| 3 | Acts by itself at scale, or hard to stop? | No: it only answers questions |
| 4 | Affects people in vulnerable circumstances? | No |
| 5 | Regulated area or legal effects? | No |
| 6 | Harm hard to undo? | No |
| 7 | General purpose or easily repurposed? | **Yes**: it is built on a general-purpose language model |

Any yes makes it **elevated**; here, questions 1 and 7. Keep the answers for the pull request.

## 5. Register it

**What it is for:** the register entry is a reviewed change in the repo, and `hound` turns it into CISO Assistant labels.

Add the system to the Example Pty Ltd domain's `systems` in `.dev/ai-walkthrough/repo/scope/setup.yaml`, after Example proposal
assistant:

```yaml
      - name: Example knowledge search
        ref_id: SYS-AI-SEARCH
        type: supporting
        description: Answers staff questions from company documents, including HR policies and staff records.
        owner: it@example.com
        ai:
          screening: elevated
          screening_yes: [1, 7]
          screened_on: "2026-11-24"
          registered_on: "2026-11-24"
          use: customised
          purpose: Help staff find answers in company documents without asking HR or IT.
          intended_uses: [Answer staff questions about company policies]
          limitations: Not for decisions about a person; answers about pay or leave must be checked with HR.
          misuse: Asking it about another staff member's records.
          data_sources: [Company policies (text), HR policies (text), Staff records (structured)]
          stakeholders: [All staff, HR team]
          oversight: human_in_the_loop
          oversight_note: Staff read every answer before acting on it; the IT lead can switch it off in its admin console.
          impact_assessment: not_started
```

Add it to the AI6 audit's systems in `scope/audits/ex-ai6-2026.yaml`:

```yaml
systems: [Example ticket triage, Example proposal assistant, Example knowledge search]
```

Check the files:

```bash
hound scope validate .dev/ai-walkthrough/repo/scope --config .dev/ai-walkthrough/repo/hound.yaml
```

It reports no problems. To see a check at work, add `impact_assessed_on: "2026-11-24"` while `impact_assessment` is still
`not_started`, validate again, and read `ai.impact_date_without_approval`; then take the line out again.

## 6. See what CISO Assistant will get, then apply it

**What it is for:** the plan is what reviewers approve on the pull request; applying writes exactly that.

```bash
hound scope plan .dev/ai-walkthrough/repo/scope --config .dev/ai-walkthrough/repo/hound.yaml
hound scope apply .dev/ai-walkthrough/repo/scope --config .dev/ai-walkthrough/repo/hound.yaml
```

The plan creates the asset Example knowledge search with the labels `ai-system`, `ai-screening-elevated`, `ai-use-customised` and
`ai-oversight-in-the-loop`, and adds it to the audit. Filter **Assets** by `ai-system` again: there are three.

## 7. Record the impact assessment

**What it is for:** an elevated system needs an approved impact assessment before it goes live (AI6-2 and AI6-3).

The owner writes the impact assessment: who it affects, what could go wrong, and how they will know. For an elevated system, record
its main risks as risk scenarios in CISO Assistant (**Risk assessments**, then a risk scenario linked to the asset Example knowledge
search). The screening template says to consult senior leadership or your governance committee about an elevated system; here that is
their approval of the impact assessment. Once they approve it, change the entry:

```yaml
          impact_assessment: approved
          impact_assessed_on: "2026-11-24"
```

## 8. Test it, then put it in use

**What it is for:** AI6-5.1 asks for a test before go-live, and the date shows when it needs testing again.

The IT team tests that it answers correctly from the right documents and refuses what staff should not see, and keeps the result as
evidence. Then record the test and the go-live date:

```yaml
          last_tested_on: "2026-11-24"
          in_use_from: "2026-11-24"
```

Validate, plan and apply again, as in steps 5 and 6. Tell staff that the search assistant is AI and how to report a wrong answer
(AI6-4).

## 9. Run hound and read the AI6 rows

**What it is for:** the `ai-register` collector checks every entry and turns the checks into AI6 results, so the self-assessment stays
current without anyone filling it in.

Write a config that runs the collector and publishes to the AI6 audit:

```bash
cat > .dev/ai-walkthrough/run.yaml <<'EOF'
ciso_assistant:
  url: http://localhost:8000/api/        # HOUND_CA_URL overrides this
  token: env:HOUND_CA_TOKEN
evidence:
  root: evidence
collectors:
  - name: example-ai-register
    type: ai-register
    schedule: "0 6 * * *"
    settings:
      scope: repo/scope
target:
  mapping: repo/mappings/ai6.yaml
  compliance_assessment: EX-AI6-2026
EOF
hound run --config .dev/ai-walkthrough/run.yaml --dry-run
hound run --config .dev/ai-walkthrough/run.yaml
```

The dry run shows the updates without sending them. After the real run, open the audit **AI6 Foundations 2026** in CISO Assistant:

- **AI6-4.1** (Maintain an AI register) is compliant: every AI system has the National AI Centre register template's columns, an impact assessment status and an
  owner.
- **AI6-5.1** (Pre-deployment testing) is not compliant, and its observation names Example proposal assistant, which went live with no
  `last_tested_on`. Your new system passes. The fix is the same as step 8, for that system.
- **AI6-3.2** (Assess AI system risks) shows whether each system in use was screened before it went live, and each elevated one had its impact assessment approved first.

The evidence (`.dev/ai-walkthrough/evidence/`) holds `ai-register.json`: every entry, whether it is in use, and the SHA-256 of the
`setup.yaml` it read, so a result traces back to the reviewed file.

## 10. Show the committee

**What it is for:** the committee wants counts and what is not met, not the detail.

```bash
hound programme list --scope .dev/ai-walkthrough/repo/scope
```

The register line now reads `Register: 3 AI systems. screening: elevated 2, normal 1. use: built 1, customised 2.` Together with the AI6 rows
that are not met, that is the AI part of the monthly report. The committee report itself arrives in sprint s12.

## 11. Clean up

**What it is for:** leaving the local stack as the example describes it.

```bash
rm -rf .dev/ai-walkthrough
make ca-down ARGS=-v && make ca-up      # -v deletes the local CISO Assistant's data
```

## What next

- Put a third-party AI system through the same steps, with its supplier approved first ([third parties](../third-parties.md)).
- Add the `ciso-assistant` profile with a read-only token, so the risk scenarios and supplier checks run too
  ([the collectors](../collectors/ciso-assistant.md)).
- Set up the yearly and quarterly AI records people attest ([AI governance](../ai-grc.md#adopting-at-foundations)).
