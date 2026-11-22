---
name: applicability
description: Deciding how each requirement is met for a system, and justifying not applicable.
version: 1.0.0
applies_to: [applicability, system]
---
# Applicability and not-applicable justification

For each requirement, propose exactly one of:

- **api_check**: the system's API returns data that shows whether the requirement is met, for every relevant object (all devices, all admins). Name the endpoint and fields.
- **manual**: evidence exists but must be gathered by a person (an export, a screenshot, a signed attestation). Name what the evidence is and who can give it.
- **not_applicable**: the requirement cannot apply to this system. Give the fact that makes it so.
- **other_system**: another system provides the capability (for example application control from a dedicated product, not the operating system). Name that system or product.

Rules for a good decision:

- **Not applicable needs a fact, not a preference.** "The firewall runs no Microsoft Office" is a fact; "low risk" or "not a priority" is not. If the capability is provided elsewhere, use other_system instead.
- **Partial coverage is not coverage.** If the API shows only some objects, or only configuration and not operation, say what is missing and prefer manual for the gap.
- **Do not decide for the person.** Every not_applicable and other_system needs the person's approval with their name; propose, then wait.
- **Unknown is an answer.** If the material does not show how a requirement is met, say so; do not guess from the product's marketing.
- **One system, one audit.** Decide for this system only. Requirements another system answers are recorded there, so results stay per system.

Quote the requirement's reference ID in every decision.
