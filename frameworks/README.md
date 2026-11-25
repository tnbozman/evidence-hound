# Framework snapshots

Each JSON file here is a snapshot of one framework library, made with `hound framework snapshot` from the library file that ships with CISO Assistant. `hound mapping validate --framework` and `hound scope validate` use them to check mappings and scope files without a running CISO Assistant. See [docs/mapping-files.md](../docs/mapping-files.md#framework-snapshots).

| File | Framework URN | Library version | From |
| --- | --- | --- | --- |
| `essential-eight-2023.json` | `urn:intuitem:risk:framework:essential-eight-2023` | 3 | CISO Assistant v4.3.4 (with descriptions and reference controls, s11-16) |
| `asd-essentials-enterprise-it.json` | `urn:intuitem:risk:framework:asd-essentials-enterprise-it` | 1 | CISO Assistant v4.3.4 (with descriptions and reference controls) |
| `mapping-essential-eight-2023-and-asd-essentials-enterprise-it.yaml` | transition mapping (not a snapshot) | 2 | CISO Assistant v4.3.4, unchanged: the library file itself, which `hound mapping derive --links` reads |
| `cmmc-2.0.json` | `urn:intuitem:risk:framework:cmmc-2.0` | 6 | CISO Assistant v4.3.4: 110 practices, 17 tagged `L1` and 93 `L2` (s17-01) |
| `disp.json` | `urn:evidence-hound:risk:framework:disp-membership` | 1 | [`disp.yaml`](disp.yaml) in this folder (s14-02) |
| `ai6.json` | `urn:evidence-hound:risk:framework:ai6` | 1 | [`ai6.yaml`](ai6.yaml) in this folder (s11-09) |

The requirement titles (shortened) and descriptions, and the transition mapping's links, are from material published by the Australian Signals Directorate on cyber.gov.au under a Creative Commons Attribution 4.0 International licence, © Commonwealth of Australia, as packaged by intuitem in CISO Assistant.

`ai6.yaml` is Evidence Hound's own library for the National AI Centre's *Guidance for AI Adoption* (AI6, v1.0, October 2025), as CISO Assistant has none. It carries the guidance's practice and sub-practice titles, © Commonwealth of Australia, with descriptions in our own words and a link to the guidance, because the guidance states no licence. See [docs/ai-grc.md](../docs/ai-grc.md).

`disp.yaml` is Evidence Hound's own DISP membership readiness library, written from the public control areas of the Defence Security Principles Framework (governance, personnel, physical, information and cyber security). Its descriptions are paraphrases in our own words, and each requirement cites its area; the DSPF and Defence's DISP guidance are authoritative. See [docs/adopt/disp-readiness.md](../docs/adopt/disp-readiness.md).

The CMMC practice titles and descriptions are from the CMMC model, © 2020, 2021 Carnegie Mellon University and The Johns Hopkins University Applied Physics Laboratory LLC, © 2021 Futures, Inc., approved for public release and licensed under the Creative Commons Attribution 4.0 International licence, as packaged by intuitem in CISO Assistant.
