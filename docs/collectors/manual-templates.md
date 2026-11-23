# Manual source templates

One template for each Essential Eight area no collector reads. Each folder holds `source.yaml`, a manual collector to paste under
`collectors:` in `hound.yaml`, and `README.md`, the runbook the owner follows and the evidence plan row points at. How they fit together
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
