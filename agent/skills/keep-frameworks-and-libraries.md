---
name: keep-frameworks-and-libraries
description: Keep the repo's frameworks, custom libraries, mappings, profiles, the organisation's own skills and these assistant files current with hound. Use when a framework or library version changes, a mapping or profile is edited, the organisation's guidance changes, or after hound is upgraded.
---
## Keep frameworks, libraries and guidance current

1. A framework library added or updated: `hound framework snapshot <library file> --out frameworks/<name>.json`, then validate the scope
   (`hound scope validate scope/ --config hound.yaml --json`) and every mapping of that framework.
2. A custom library (risk matrix, threats) changed: raise its `version`, then `hound library validate <library file>` and
   `hound library snapshot <library file> --out libraries/<name>.json`.
3. A mapping changed: `hound mapping validate mappings/<file>.yaml`. For a new framework version, draft its mapping from the transition
   mapping with `hound mapping derive <mapping>`; every row it writes is `draft: true` until a person reviews it, and draft rows are never
   published.
4. A REST profile changed: `hound profile validate <name>`, `hound profile test <name>` (offline, against its recorded tests), and
   `hound profile diff <name>` to see how a copied profile differs from the library's.
5. The organisation's own guidance: `hound skill list` shows the active skills; `hound skill draft` writes the organisation's profile,
   risk appetite, IT and AI landscape as templates for the person to fill in; `hound skill new <name>` starts another; check with
   `hound skill validate` and copy one in with `hound skill add <file>`.
6. After hound is upgraded, refresh these assistant files: `hound agent setup --assistant both --repo .`. It replaces only the files
   hound wrote (they carry its marker) and keeps the person's own.
