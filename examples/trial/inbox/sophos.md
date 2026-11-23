# Sophos: supplier notes (fictional)

Fictional notes for the Evidence Hound trial, written as Example Pty Ltd's GRC team would keep them. They are not Sophos's documents
and make no claim about Sophos's real certifications: in a real review, keep the supplier's own reports under `documents/suppliers/`.

- **What we buy:** the office's XGS 88W firewall with its subscription, managed in Sophos Central (contract CTR-SOPHOS-2026).
- **What depends on it:** OFFICE-FIREWALL carries all office internet traffic and the site-to-site VPN to GRC-PLATFORM.
- **Access:** Sophos Central manages the firewall remotely (firmware, configuration backups). Our admins sign in to Sophos Central;
  Evidence Hound reads it with a read-only API credential.
- **API:** the Sophos Central API; its description for the trial is `../specs/sophos-central.openapi.yaml`.
- **Assurance seen:** none kept yet. The vendor publishes security advisories; ask the person whether to keep any report.
