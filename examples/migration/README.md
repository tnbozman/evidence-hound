# Sample spreadsheet registers

Fictional registers of Example Pty Ltd, exported to CSV the way a small team keeps them before it adopts Evidence Hound. The
[migration guide](../../docs/adopt/migrate-from-spreadsheets.md) moves them into the example compliance repo, row by row.

| File | Register | Moved in with |
| --- | --- | --- |
| `systems.csv` | Systems and assets | `hound add system` |
| `controls.csv` | Security controls, with the Essential Eight requirements each supports | `hound add control` |
| `suppliers.csv` | Suppliers and their contracts | `hound supplier draft`, then a person runs `hound approve` |

Audit results move in with `hound scope import`; see [`examples/workbooks/`](../workbooks/README.md).
