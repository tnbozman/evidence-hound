# Framework snapshots

Each JSON file here is a snapshot of one framework library, made with `hound framework snapshot` from the library file that ships with CISO Assistant. `hound mapping validate --framework` and `hound scope validate` use them to check mappings and scope files without a running CISO Assistant. See [docs/mapping-files.md](../docs/mapping-files.md#framework-snapshots).

| File | Framework URN | Library version | From |
| --- | --- | --- | --- |
| `essential-eight-2023.json` | `urn:intuitem:risk:framework:essential-eight-2023` | 3 | CISO Assistant v4.3.4 (with descriptions and reference controls, s11-16) |
| `asd-essentials-enterprise-it.json` | `urn:intuitem:risk:framework:asd-essentials-enterprise-it` | 1 | CISO Assistant v4.3.4 (with descriptions and reference controls) |
| `ai6.json` | `urn:evidence-hound:risk:framework:ai6` | 1 | [`ai6.yaml`](ai6.yaml) in this folder (s11-09) |

The requirement titles (shortened) and descriptions are from material published by the Australian Signals Directorate on cyber.gov.au under a Creative Commons Attribution 4.0 International licence, © Commonwealth of Australia, as packaged by intuitem in CISO Assistant.

`ai6.yaml` is Evidence Hound's own library for the National AI Centre's *Guidance for AI Adoption* (AI6, v1.0, October 2025), as CISO Assistant has none. It carries the guidance's practice and sub-practice titles, © Commonwealth of Australia, with descriptions in our own words and a link to the guidance, because the guidance states no licence. See [docs/ai-grc.md](../docs/ai-grc.md).
