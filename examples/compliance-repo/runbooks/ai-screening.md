# Runbook: screening a new AI system

Use this when someone proposes a new AI tool, switches on an AI feature in a product you already use, or changes how an AI system is
used. It takes about 20 minutes. The result is a pull request that adds or changes the system's `ai` block in `scope/setup.yaml`.
Everything here is fictional; adapt it to your organisation.

These questions follow the six AI6 practices. They will be aligned with the National AI Centre's own screening template once it is in
the repo (sprint s11, task s11-11).

## 1. Is it an AI system?

It is, if it makes predictions, recommendations, content or decisions from data with some autonomy: a chat assistant, an AI feature
in a software-as-a-service product, a scoring or triage model. If not, stop here.

## 2. Supplier first

If a supplier provides it, the supplier must be approved before the system is registered, as for any other system. Note the
supplier's solution `ref_id`; the system names it as `solution`, and its `use` is `third_party`.

## 3. Screening questions

Answer each with yes or no, and keep the answers with the pull request.

| # | Question | A yes means |
| --- | --- | --- |
| 1 | Is it used for something the law or your policy forbids, such as social scoring or covert surveillance of staff? | **prohibited**: do not use it |
| 2 | Does it make or materially shape decisions about people (hiring, pay, access to a service, credit, discipline)? | at least **medium** |
| 3 | Does it handle sensitive or personal information (CUI, PII, health, export-controlled, classified)? | at least **medium** |
| 4 | Does it act without a person reviewing each output before it takes effect? | at least **medium** |
| 5 | Could a wrong output cause physical harm, a safety incident, or a reportable cyber or privacy incident? | **high** |
| 6 | Does it make decisions about people *and* act without a person reviewing each one? | **high** |

With no yes, the tier is **low**. Take the highest tier any answer gives.

## 4. Record it

Add or change the `ai` block (see `docs/scope.md`, AI systems):

- `tier` and `screened_on` (today).
- `use`: `built`, `customised` or `third_party`.
- `oversight`: `human_in_the_loop` (a person approves each output), `human_on_the_loop` (it acts alone while a person watches and can
  step in) or `human_out_of_loop`. A **high** tier system must have a person in or on the loop.
- `oversight_note`: who can override or switch it off, and what you fall back to.
- `impact_assessment`: `not_required` for **low**. For **medium** and **high**, `not_started` until the impact assessment is approved,
  then `approved` with `impact_assessed_on`. It must be approved before `in_use_from`.
- For **medium** and **high**, record its risks as risk scenarios linked to its asset in CISO Assistant.

## 5. Before it goes live

- Test that it works as intended for your use, keep the result as evidence, and set `last_tested_on`.
- Set `in_use_from` when it goes live.
- Tell staff and customers where they deal with it or its output (AI6-4).

## 6. Review the pull request

The GRC team reviews the pull request, `hound scope plan` shows the labels and description CISO Assistant will get, and the next run
checks the entry.
