# Answer a prime's questionnaire from the evidence you already have

Primes and customers send security questionnaires, often several a year, each asking about the same controls in different words. A US
prime may ask about NIST SP 800-171 even though you are assessed against the Essential Eight. Answer them all from one evidence set:
your audit of record in CISO Assistant.

## 1. See what your Essential Eight evidence already answers

```console
$ hound mapping crosswalk --from essential-eight --config hound.yaml > crosswalk.md
```

The command, explained in [mapping files](../mapping-files.md#crosswalks-to-another-framework), lists every NIST SP 800-171
requirement with the Essential Eight requirements that answer it and today's result of each, from your audit of record:

- **Answered**: point the prime at the Essential Eight evidence named.
- **Partly answered**: the Essential Eight covers part of it (for example workstations but not every system holding CUI). Answer with
  what it covers, and add what else you do.
- **Open**: the Essential Eight does not cover it (for example physical security or personnel screening). You need other evidence, or
  the honest answer "not yet".

The crosswalk is advisory: it was drafted with AI from the requirement texts and nobody has reviewed it yet. Check the links you rely
on before you send anything.

## 2. Answer the questionnaire with your agent

Give your agent the questionnaire (a spreadsheet or a PDF) and the crosswalk, and ask:

> For each question in `questionnaire.xlsx`, find the NIST SP 800-171 requirement it asks about, if any, in `crosswalk.md`. Draft an
> answer from the Essential Eight evidence and today's result named there, and say which requirement and result each answer rests on.
> Where the crosswalk says open, or the result is not compliant, say so plainly and do not claim more. Mark every answer you are
> unsure of.

Then read every answer yourself. Change any that claims more than the evidence shows.

## 3. Give verifiable proof, not screenshots

Instead of a screenshot per question, offer the prime the same records your committee sees:

- the audit of record's export from CISO Assistant (the requirement results with their observations and evidence names);
- the month's posture snapshot from `hound report snapshot`, which lists each result and when it was collected;
- for a requirement they care about, the evidence revision in CISO Assistant, with its date and hash.

Every result names the check or the person that produced it and when, so the prime can see it is current. Agree with the prime how
often they need it refreshed, and send the next month's snapshot rather than answering again.

## 4. Keep the answers

Save the completed questionnaire as evidence in CISO Assistant (on your "Security reporting" applied control, or a control for customer
assurance), so the next prime's questionnaire starts from it.
