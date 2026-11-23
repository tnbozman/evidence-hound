---
name: environment-capabilities
description: Deciding which product in the environment is authoritative for each mitigation, before systems are onboarded.
version: 1.0.0
applies_to: [environment]
---
# Which product provides each mitigation

The aim is one decision per mitigation: the product whose own settings and records prove it, so `hound` only judges a requirement on that product's audit, and never fails an audit because it looked at the wrong system.

- **The authoritative product is the one that enforces it.** Owning a licence is not using it. Microsoft 365 E3 includes Intune, but if application control on the laptops is enforced by a third-party product, that product provides application control and the Microsoft 365 audit marks it not applicable, naming the product.
- **Signals suggest, people decide.** Licences, Intune configuration kinds and tagged systems pre-fill an answer and are recorded with their source. Accept a suggestion only when you know the product is configured and in use.
- **Common products, by Essential Eight mitigation:**
  - Application control (`ac`): Intune with App Control for Business or AppLocker, or a third-party product such as an allow-listing agent. Evidence: the policy, its mode (enforced or audit) and where it is assigned; blocked-execution events.
  - Patch applications and operating systems (`pa`, `pos`): a vulnerability scanner (Defender for Endpoint vulnerability management or a third-party scanner) for discovery; Intune update rings or a patch management product for deployment. Evidence: scan coverage and age, missing patches by age.
  - Office macros and user application hardening (`rmom`, `uah`): Intune configuration or group policy. Evidence: the settings and their assignment.
  - Restrict administrative privileges and MFA (`rap`, `mfa`): the identity provider, usually Microsoft Entra ID. Evidence: role holders, privileged identity management, Conditional Access and MFA registration.
  - Regular backups (`rb`): the backup product. Evidence: jobs, retention, restore tests and who can delete backups.
- **Manual or gap are honest answers.** Manual means evidence is gathered by hand on a schedule. A gap means nothing provides it yet: it is reported as a gap, not as a failure of some other system.
- **Never record secrets, host names, addresses or people's names.** Refer to roles and to systems by their names in scope.
