# Runbook: yearly CMMC Level 2 affirmation

CISO Assistant emails this to the affirming official (Sam Example) a little before the anniversary of the last CMMC Level 2
assessment. Everything here is made up.

## What to export

1. Ask the GRC team for this year's affirmation pack. They take the month's snapshot and build it:

   ```sh
   hound report snapshot --period 2026-11 --config hound.yaml
   hound sprs affirmation-pack --programme cmmc-level-2 --period 2026-11 --config hound.yaml --out affirmation-2026.md
   ```

2. Read it: the SPRS score, every open POA&M item with its owner and date, any item CMMC does not allow on a POA&M, stale
   evidence and lapsed manual sources. Ask about anything you cannot accept.
3. Check the score in SPRS matches the pack, or ask the GRC team to update SPRS with the reviewed score first.

## Where to affirm

Affirm continuing compliance yourself in SPRS. Nobody else, and no agent, affirms for you.

## Send it to the GRC team

Send the GRC team the date you affirmed and the pack you read. They attach both to this year's task in CISO Assistant at the
weekly review, which records the affirmation as evidence on the control AC-SPRS.
