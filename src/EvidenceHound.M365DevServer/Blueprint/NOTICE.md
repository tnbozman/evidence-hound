# ASD Blueprint for Secure Cloud material

The files in this folder come from the ASD Blueprint for Secure Cloud, published by the Australian Signals Directorate.

- Source: https://github.com/ASD-Blueprint/ASD-Blueprint-for-Secure-Cloud/tree/a6e1e5f95e6051f23d2e7d1148c8fd57a6614000/static/content/files
- Commit: a6e1e5f95e6051f23d2e7d1148c8fd57a6614000
- Licence: Creative Commons Attribution 4.0 International (https://creativecommons.org/licenses/by/4.0/)
- Attribution: © Commonwealth of Australia 2023.

| File here | Published as | Changes |
| --- | --- | --- |
| `OfficeHardening.txt` | `intune-config-policies/OfficeHardening.txt` | None. The dev server adds an assignment to all users when it serves the policy, because an export carries none. |

Translated by hand, not copied (the source is a Microsoft365DSC configuration, not a Graph response):

| Served as | Translated from | Changes |
| --- | --- | --- |
| Settings catalog policy "ASD Windows Hardening Guidelines - Attack Surface Reduction" | `automation/dsc/BlueprintDefenderDsc.txt`, resource `IntuneSettingCatalogASRRulesPolicyWindows10` | Every rule the configuration sets to block, as Graph settings catalog choices. |
| Conditional Access "USR - G - Require strong auth" | `automation/dsc/BlueprintEntraDsc.txt` | State `enabled` instead of report-only; the excluded group `$ConditionalExclude` is replaced by the two break-glass accounts. |
| Conditional Access "USR - B - Block access via legacy auth" | `automation/dsc/BlueprintEntraDsc.txt` | As above. |

The users, devices and role assignments are fictional (Example Pty Ltd, `example.com`) and are not ASD material.
