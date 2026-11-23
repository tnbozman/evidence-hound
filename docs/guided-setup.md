# Guided setup: hound init and hound add

`hound` can write a compliance repo's files for you. `hound init` scaffolds a new organisation, and `hound add` adds a system, a control or a collector. Each asks only the questions it needs, shows the default in brackets (press Enter to take it), and checks the result the way `hound config validate` and `hound scope validate` do before it writes anything. A change that would not validate is not written: `hound` reports the problems, with exit code 3, and leaves the files as they were.

Every question has an option, so a wizard also runs unattended: give the answers as options and add `--yes`, and it asks nothing, taking the defaults for anything not given. Questions go to stderr, so the output stays clean.

The [local trial](tutorials/local-trial.md) uses these wizards from start to finish.

## hound init

```bash
hound init compliance
```

Writes a new compliance repo into the folder, which must be missing or empty:

| Question | Option | Default |
| --- | --- | --- |
| Organisation name, also its domain in CISO Assistant | `--org` | Example Pty Ltd |
| Short code for ref IDs, such as EX in EX-E8-2027 | `--code` | from the name |
| CISO Assistant API address | `--url` | `https://ciso.example.com/api/` |
| Time zone schedules run in | `--time-zone` | Australia/Sydney |
| Essential Eight maturity level to assess, 1 to 3 | `--level` | 2 |
| Audit due date | `--due` | 30 June next year |
| Compliance owner's email | `--owner` | it@example.com |

`--evidence-root` sets where the runner keeps evidence (default `/var/lib/evidence-hound/evidence`, outside the repo).

It writes:

| File | Holds |
| --- | --- |
| `hound.yaml` | The deployment config: CISO Assistant, evidence, the schedule's time zone, no collectors yet, and the Essential Eight target with its health control |
| `scope/setup.yaml` | The root folder, the Essential Eight framework with its snapshot, and one domain with its perimeter |
| `scope/controls.yaml` | The health control, AC-AUTO-EVIDENCE |
| `scope/audits/<code>-e8-<year>.yaml` | The audit at that maturity level, with no hand-written results yet. `hound scope new-audit` lists every requirement, if you want them all in the file |
| `mappings/essential-eight-2023.yaml`, `frameworks/essential-eight-2023.json` | The Essential Eight mapping and framework snapshot of this `hound` version |
| `.gitignore`, `README.md` | What never goes in the repo, and the next steps |

For access, settings and a pull request workflow, copy them from the [example compliance repo](../examples/compliance-repo/README.md), as the [first deployment tutorial](tutorials/first-deployment.md) describes.

At the end `hound init` asks whether to set up a coding assistant in the new repo (`claude`, `copilot`, `both` or `none`; `--assistant` answers it, and `--yes` takes `none`). It writes what [`hound agent setup`](onboarding.md#set-up-your-coding-assistant-hound-agent-setup) writes.

## hound add system

```bash
hound add system --scope scope --audit EX-E8-2027
```

Adds a system to a domain in `scope/setup.yaml`: its name, ref ID (default `SYS-` and the name), type (`primary` or `supporting`), owner, description and the systems it supports (`--name`, `--ref-id`, `--type`, `--owner`, `--description`, `--parents`). The domain is asked only when the scope has more than one (`--domain`). Each `--audit` adds the system to that audit's `systems`.

## hound add control

```bash
hound add control --scope scope --audit EX-E8-2027
```

Declares an applied control in `scope/controls.yaml`: its ref ID, name, owner, category (default `technical`) and the systems it covers (`--ref-id`, `--name`, `--owner`, `--category`, `--systems`), with status `to_do` until the first apply. With `--audit`, it asks which of that audit's requirements the control supports (`--requirements ISM-1511,ISM-1515`) and links them in the audit file. A requirement the framework does not have is refused.

## hound add collector

```bash
hound add collector manual --config hound.yaml
hound add collector rest --name sophos --set profile=sophos-central --config hound.yaml --secrets-dir /etc/evidence-hound/secrets
```

Adds a collector of any type `hound collectors` lists. It asks for the collector's name (default: the type), its schedule (default `0 3 * * *`) and each setting the collector requires, using the collector's own description of it. `--set key=value` gives any setting, optional ones included, without asking. For the `rest` collector, once it knows the profile (`--set profile=<name>`, and `--set profiles_path=<folder>` when the profiles folder is not the default), it also asks for each parameter the profile requires and has no default for, and takes the profile's secrets as the collector's.

For each secret the collector needs:

- Without `--secrets-dir`, it asks for a reference, by default `file:/run/secrets/<name>_<secret>`, the path the runner container sees. Put the value in that file on the host yourself.
- With `--secrets-dir <folder>`, it reads the value instead, without echo on a terminal, writes it to `<folder>/<name>_<secret>` (folder mode 700, file mode 600) and references that file. The value is never printed, logged or put on a command line.

`hound add collector manual` is how a manual evidence source is added: its drop folder, the checks it supports, how often fresh evidence is due and who owns it. See [manual sources](configuration.md#manual-sources).

## How the files are changed

An added entry goes at the end of its list, in the file's own indentation. Comments, blank lines and every other line stay as they were, so the change reads as a small diff in review. An empty `[]` list becomes a block list, and a missing list is added. Values are quoted only where YAML would read them as something else, such as `"0 3 * * *"` or `"14"`.
