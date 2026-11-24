# Runbook: screening a new AI system

Use this when someone proposes a new AI tool, switches on an AI feature in a product you already use, or changes how an AI system is
used. It takes about 20 minutes. The result is a pull request that adds or changes the system's `ai` block in `scope/setup.yaml`.
Everything here is fictional; adapt it to your organisation.

The questions are the National AI Centre's seven screening questions (*AI screening questions*, ai.gov.au), put in our own words;
`docs/ai-grc.md` traces each one. Read the template itself for its examples.

## 1. Is it an AI system?

It is, if it makes predictions, recommendations, content or decisions from data with some autonomy: a chat assistant, an AI feature
in a software-as-a-service product, a scoring or triage model. If not, stop here.

## 2. Supplier first

If a supplier provides it, the supplier must be approved before the system is registered, as for any other system. Note the
supplier's solution `ref_id`; the system names it as `solution`, and its `use` is `third_party`.

## 3. Screening questions

Answer each with yes or no for the use you are proposing, and keep the answers with the pull request.

| # | In short | Yes when, for example |
| --- | --- | --- |
| 1 | Personal, sensitive or confidential information | It is trained on, processes or is prompted with customer or staff details, health or financial records, or company confidential information. |
| 2 | Talks to people without a person checking | It answers customers or staff in its own words, beyond fixed scripts, with no one reviewing the reply. |
| 3 | Acts by itself at scale, or is hard to stop | An agent that takes actions across systems or on the internet, or a process too large for a person to oversee and step into. |
| 4 | Affects people in vulnerable circumstances | It makes or supports decisions about children, older people, people with disability, First Nations people, or other groups at risk of being overlooked. |
| 5 | Regulated area or legal effects | Credit or financial approvals, health advice, eligibility for a service. |
| 6 | Harm hard to undo | A wrong output would be hard to challenge, reverse or put right: damaged relationships or reputations, lost opportunities, irreversible actions. |
| 7 | General purpose or easily repurposed | It is built on a general-purpose model, takes open-ended instructions, or cannot explain how it decides. |

The outcome:

- **normal**: no to every question. Follow your usual adoption steps.
- **elevated**: yes to any question. Consult senior leadership or your AI governance committee before planning it in detail. Here that
  means the impact assessment, approved by them, before it goes live.
- **unacceptable**: the committee decides the risk cannot be accepted, whatever the answers. Do not use it.

`hound scope validate` reports `ai.screening_disagrees` when the outcome does not follow from the answers (normal with a yes, or
elevated with none).

## 4. Record it

Add or change the `ai` block (see `docs/scope.md`, AI systems). It holds the National AI Centre register template's columns:

- `screening` (`normal`, `elevated` or `unacceptable`), `screening_yes` (the numbers answered yes, such as `[1, 7]`, or `[]`) and
  `screened_on` (today).
- `registered_on`: the day it is approved and added to the register.
- `version`, `purpose`, `intended_uses`, `limitations` (known limitations and prohibited use), `misuse` (foreseeable misuse, or "None
  identified"), `data_sources` (each source and its type) and `stakeholders` (who it affects).
- `use`: `built`, `customised` or `third_party` (with `solution`, for the template's *Source* column).
- `oversight`: `human_in_the_loop` (a person approves each output), `human_on_the_loop` (it acts alone while a person watches and can
  step in) or `human_out_of_loop`. An **elevated** system must have a person in or on the loop.
- `oversight_note`: who can override or switch it off, and what you fall back to.
- `impact_assessment`: `not_required` for **normal**. For **elevated**, `not_started` until the impact assessment is approved, then
  `approved` with `impact_assessed_on`. It must be approved before `in_use_from`.
- For **elevated**, record its risks as risk scenarios linked to its asset in CISO Assistant.

The register's *Status* column follows from the dates: pilot before `in_use_from`, operational while in use, decommissioned after
`retired_on`.

## 5. Before it goes live

- Test that it works as intended for your use, keep the result as evidence, and set `last_tested_on`. Test again at least every
  12 months, or 6 for an elevated system.
- Set `in_use_from` when it goes live.
- Tell staff and customers where they deal with it or its output (AI6-4).

## 6. Review the pull request

The GRC team reviews the pull request, `hound scope plan` shows the labels and description CISO Assistant will get, and the next run
checks the entry.
