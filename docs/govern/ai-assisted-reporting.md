# Drafting reports with AI

hound writes every figure in the committee and board reports itself, from CISO Assistant, with fixed rules. An AI assistant can help
with the words around them: the committee summary, the board's compliance statements, and the questions members will ask. This guide
shows how to use your own agent (Claude Code or Copilot, set up with `hound agent setup`) safely.

## The rule

**Every figure comes from the generated report, not from the model.** The assistant may reword, shorten, order and explain. It may not
add, round, estimate or change a number, a result, a date or a name. If it says something the report does not, delete it.

## Set up

1. Give the agent the report and its snapshot, nothing else: the Markdown file from `hound report committee` or `hound report board`,
   and the snapshot JSON from the evidence store's `reports/` folder.
2. If you want it to look up detail in CISO Assistant, give it a **read-only** account: in CISO Assistant, create a user with the
   Reader role on the folders in scope, create its personal access token, and give the agent `HOUND_CA_URL` and that token. Never give
   it hound's own service-account token, which can write. If your CISO Assistant version offers an MCP server, connect it with the same
   read-only account.
3. Keep the agent's work in the compliance repo or a scratch folder, not in CISO Assistant. You publish the final report yourself.

## Prompts

**The committee summary.**

> Read `committee-2026-11.md`. Rewrite section 1 (Summary) as at most five short sentences for busy managers: what got better, what got
> worse, and what the committee must decide. Use only facts and figures that appear in the report, and keep every figure exactly as
> written. After the summary, list each sentence with the report line it came from.

**The board compliance positions.**

> Read `board-2026-q4.md`. For each programme in section 1, write two sentences the authorising official can read out: the position and
> the main reason, in plain English without acronyms. Do not change the position or any figure. Quote the report line for each fact.

**Questions to expect.**

> Read the report. List the five questions a sceptical committee member is most likely to ask, and where in the report, or in CISO
> Assistant through the read-only account, the answer is. Do not answer from your own knowledge.

## Check before you send

- Every sentence has a report line behind it; the agent listed them, so tick them off.
- Figures match the report exactly. The fastest check is to search the report for each number.
- Nothing names a person who is not already in the report.
- The final report is the one hound generated, with only the summary or positions replaced. Upload that file as the "Security
  reporting" evidence revision.

## Why it is done this way

Reports go to people who make decisions on them. A model can write fluent text that is wrong. Keeping the figures deterministic, and the
model to wording only, with every claim traced to a line, keeps the report true and makes the check quick. This is the same rule hound
follows everywhere: no model in collection, no evidence without a person.
