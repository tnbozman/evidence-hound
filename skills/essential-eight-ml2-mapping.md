---
name: essential-eight-ml2-mapping
description: Mapping system evidence to Essential Eight Maturity Level Two requirements.
version: 1.0.0
applies_to: [applicability, mapping]
frameworks: [essential-eight-2023]
---
# Mapping to Essential Eight Maturity Level Two

The Essential Eight's mitigation strategies are patch applications, patch operating systems, multi-factor authentication, restrict administrative privileges, application control, restrict Microsoft Office macros, user application hardening and regular backups. Each has requirements at each maturity level.

- **Use the requirement text you are given.** The framework snapshot in the material is the source of truth for wording, reference IDs and maturity levels. Do not restate requirements from memory, and never mix in Maturity Level One or Three requirements.
- **Map to the most specific requirement.** A check of firmware versions supports the operating-system patching requirements for that device class, not "patch applications".
- **Time limits matter.** Many patching and review requirements have a time limit. A check passes only if the data shows the limit is met (for example the patch release date, or the last review date), not only that a value is set.
- **Coverage matters.** A requirement about "all" workstations, servers or privileged users is met only when the check covers every one in scope. Say which objects the check cannot see.
- **Where a requirement belongs.** Application control, macro settings and user application hardening usually belong to the endpoint management or a dedicated product, not to network devices. Patching, MFA and privileged access apply to most systems, including network devices and their management consoles.
- **Evidence over configuration.** Prefer data that shows the control operating (an event, a version, a last-run date) over a setting that says it should.

Name the requirement's reference ID and the field or evidence that shows it.
