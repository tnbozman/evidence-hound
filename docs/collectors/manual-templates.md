# Manual source templates

One template for each Essential Eight area no collector reads. Each folder holds `source.yaml`, a manual collector to paste under
`collectors:` in `hound.yaml` (a repo from `hound init` starts with `collectors: []`: replace it with `collectors:` first), and `README.md`, the runbook the owner follows and the evidence plan row points at. How they fit together
is in [Manual evidence](../manual-evidence.md).

| Strategy | Template | Every | Requirements |
| --- | --- | --- | --- |
| Application control | [Application control review](../../examples/manual-sources/essential-eight/application-control-review/README.md) | 12 months | ISM-1490, ISM-1544, ISM-1582, ISM-1870, ISM-1871 |
| Multi-factor authentication | [Multi-factor authentication configuration](../../examples/manual-sources/essential-eight/mfa-configuration/README.md) | 6 months | ISM-0974, ISM-1401, ISM-1679, ISM-1680, ISM-1681, ISM-1682, ISM-1872, ISM-1873, ISM-1892, ISM-1893 |
| Multi-factor authentication, Restrict administrative privileges, Application control, User application hardening | [Event logging and analysis](../../examples/manual-sources/essential-eight/event-logging/README.md) | 3 months | ISM-1228, ISM-1509, ISM-1623, ISM-1650, ISM-1683, ISM-1815, ISM-1889, ISM-1906 |
| Patch applications, Patch operating systems | [Asset discovery and vulnerability scanner](../../examples/manual-sources/essential-eight/asset-discovery/README.md) | 3 months | ISM-1807, ISM-1808 |
| Regular backups | [Backups](../../examples/manual-sources/essential-eight/backups/README.md) | 3 months | ISM-1511, ISM-1705, ISM-1707, ISM-1810, ISM-1811, ISM-1812, ISM-1814 |
| Restrict Microsoft Office macros | [Microsoft Office macro settings](../../examples/manual-sources/essential-eight/office-macro-settings/README.md) | 6 months | ISM-1489, ISM-1671, ISM-1672 |
| Restrict administrative privileges | [Privileged access review](../../examples/manual-sources/essential-eight/privileged-access-review/README.md) | 3 months | ISM-1175, ISM-1380, ISM-1387, ISM-1507, ISM-1647, ISM-1648, ISM-1685, ISM-1687, ISM-1688, ISM-1689 |
| User application hardening | [User application hardening](../../examples/manual-sources/essential-eight/user-application-hardening/README.md) | 6 months | ISM-1412, ISM-1485, ISM-1486, ISM-1542, ISM-1585, ISM-1654, ISM-1670, ISM-1823, ISM-1824, ISM-1859, ISM-1860 |

Every check in these templates is mapped in `mappings/essential-eight-2023.yaml`. Copy a template into your compliance repo when you
take the area on; until then its requirements are yours to write by hand in the audit file.

## ASD Essentials (s14)

For the Essentials actions with no Essential Eight source, in `examples/manual-sources/essentials/`. Each check is mapped in
`mappings/asd-essentials-enterprise-it.yaml`, to the action in every priority phase 1 domain that rates it. The Essential Eight templates
above cover the actions the Essential Eight already evidences.

| Template | Every | Actions |
| --- | --- | --- |
| [Asset records](../../examples/manual-sources/essentials/asset-records/README.md) | 3 months | IT.IDE-01.2 |
| [Content filtering](../../examples/manual-sources/essentials/content-filtering/README.md) | 3 months | IT.PRO-09.1, IT.PRO-09.2, IT.PRO-09.3 |
| [Cyber governance](../../examples/manual-sources/essentials/cyber-governance/README.md) | 12 months | IT.GOV-03.1, IT.GOV-04.1, IT.GOV-04.2, IT.GOV-08.1, IT.IDE-04.1 |
| [Incident learning and detection](../../examples/manual-sources/essentials/incident-learning/README.md) | 6 months | IT.DET-02.1, IT.RES-04.1, IT.RES-04.2 |
| [Network segmentation](../../examples/manual-sources/essentials/network-segmentation/README.md) | 6 months | IT.PRO-18.1 |
| [Secure administration](../../examples/manual-sources/essentials/secure-administration/README.md) | 6 months | IT.PRO-05.2 |
| [Secure build and secrets](../../examples/manual-sources/essentials/secure-build-and-secrets/README.md) | 3 months | IT.PRO-01.1, IT.PRO-01.2, IT.PRO-13.2, IT.PRO-13.3 |
| [Security awareness training](../../examples/manual-sources/essentials/security-awareness-training/README.md) | 12 months | IT.PRO-14.1, IT.PRO-14.2 |
| [Supply chain security](../../examples/manual-sources/essentials/supply-chain-security/README.md) | 3 months | IT.PRO-16.1, IT.PRO-16.2 |

## DISP membership readiness (s14)

For the records no system holds, in `examples/manual-sources/disp/`. Each check is mapped in `mappings/disp.yaml`. Security awareness
training (DISP-PER-04) uses the Essentials [security awareness training](../../examples/manual-sources/essentials/security-awareness-training/README.md)
template, so one set of training records answers both.

| Template | Every | Requirements |
| --- | --- | --- |
| [Annual security report to Defence](../../examples/manual-sources/disp/annual-security-report/README.md) | 12 months | DISP-GOV-04, DISP-INF-03, DISP-PER-05 |
| [Clearances register review](../../examples/manual-sources/disp/clearances-register/README.md) | 6 months | DISP-PER-01, DISP-PER-02, DISP-PER-03 |
| [Facility and visitor controls](../../examples/manual-sources/disp/facility-security/README.md) | 6 months | DISP-PHY-01, DISP-PHY-02, DISP-PHY-03 |
| [Security officer and governance](../../examples/manual-sources/disp/security-officer/README.md) | 12 months | DISP-GOV-01, DISP-GOV-02, DISP-GOV-03 |
