---
name: review-before-apply
description: Explain what a hound plan or config change will do in CISO Assistant, and flag what needs a careful look, before a person applies it. Use when asked to review a plan, a diff or a pull request in this repo.
includes: [reviewing-a-config-change]
---
## Review before apply

1. Validate first: `hound scope validate scope/ --config hound.yaml --json`. A change that does not validate is not ready to review.
2. Get the plan: `hound scope plan scope/ --config hound.yaml --json`. Without access to CISO Assistant, review the diff instead and say the plan is still to be read.
3. Get what code reads from it: `hound explain <saved plan or diff> --repo . --json` gives the counts, the objects and the flags. Quote its numbers; never recount them.
4. Write the review for the person who must approve it. You change no file and you never apply.
