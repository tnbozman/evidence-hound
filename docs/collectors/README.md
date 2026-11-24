# Collectors

A collector reads one kind of system, read-only, and reports framework-neutral checks with the evidence behind each. The mapping files
turn checks into requirement results. Each guide lists the read-only permissions the collector needs.

| Collector | Type | Reads | Guide |
| --- | --- | --- | --- |
| Microsoft 365 | `m365` | Entra ID, Intune, Defender and Office settings through Microsoft Graph | [Microsoft 365](m365.md) |
| Any REST API | `rest` | A YAML profile from your compliance repo | [REST profiles](rest.md) |
| Sophos Central | `rest`, profile `sophos-central` | Endpoints, policies and firewalls | [Sophos Central](sophos.md) |
| Terraform tests | `rest`, profile `terraform-tests` | Your IaC pipeline's control test results | [Terraform tests](terraform-tests.md) |
| Terraform drift | `rest`, profiles `terraform-drift` and `terraform-drift-trigger` | Your IaC pipeline's drift plan | [Terraform drift](terraform-drift.md) |
| CISO Assistant | `ai-register` and `rest`, profile `ciso-assistant` | The AI register, suppliers and risks | [CISO Assistant](ciso-assistant.md) |
| Vulnerability scans | `vuln-scan` | Nessus or CSV exports dropped in a folder | [Vulnerability scans](vuln-scan.md) |
| Manual evidence | `manual` | Files people export, attested by a person | [Manual evidence](../manual-evidence.md), [templates](manual-templates.md) |

No collector here for your system? [Write a profile](../tutorials/write-a-profile.md) for its REST API, or, rarely,
[a collector](../tutorials/write-a-collector.md).
