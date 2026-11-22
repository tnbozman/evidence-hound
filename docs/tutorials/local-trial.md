# Local trial: Evidence Hound next to your CISO Assistant

This tutorial is interactive: `make trial` runs it step by step from the dev container. It takes Evidence Hound from nothing to results in CISO Assistant with `hound`'s own wizards ([guided setup](../guided-setup.md)): a new organisation (the fictional Example Pty Ltd, if you press Enter through the questions), a system, a control and a manual evidence source, and then your own Sophos Central tenant. You need no server and no Microsoft 365 tenant. Allow an hour, most of it the first image build.

```bash
make trial
```

Each step shows the `hound` command before it runs and asks before anything that changes CISO Assistant. Answer `q` to stop. Run `make trial` again to carry on: finished steps are skipped. `make trial-clean` removes the workspace and leaves CISO Assistant running.

## What it needs

- The dev container, with Docker answering inside it.
- A CISO Assistant. The dev container starts its own, at `http://backend:8000` in the dev container and `https://localhost:8443` in your browser. A stack from `make ca-up` works too. If none is running, the trial offers to start one.
- For the last step: a Sophos Central API credential with the **Service Principal Read-Only** role, created as [the Sophos guide](../collectors/sophos.md) describes.

## 1. Preflight

Before it starts anything, the trial checks:

| Check | What happens |
| --- | --- |
| Docker answers | Stops with a hint if it does not. |
| The dev container's CISO Assistant answers at `http://backend:8000` | Reused. Nothing new is started. |
| A CISO Assistant backend container runs on the Docker daemon the dev container sees (`make ca-up`) | Reused, on its Docker network. |
| Something answers at `http://localhost:8000` | Reused. |
| None of these, and ports 8000 and 8443 are free | Offers `make ca-up`. |
| None of these, and port 8000 or 8443 is taken | Stops and names the container, or says it is a process outside Docker. |

## 2. The runner image

The trial builds the runner image from your checkout (`make image HOUND_IMAGE=evidence-hound-runner:trial`), so it tests your branch, not a published release. Every `hound` command then runs as a throwaway container: `docker run --rm` with the workspace mounted at `/trial`.

How the container reaches CISO Assistant:

- **The dev container's CISO Assistant** runs on the Docker daemon outside the dev container. The dev container uses Docker-in-Docker, so its containers cannot join that network. They share the dev container's network namespace (`--network host`) instead, where `backend` resolves.
- **A `make ca-up` stack** runs on the dev container's own daemon. `hound` joins its network (`hound-ciso-assistant_default`).

In both cases the API is `http://backend:8000/api/`. The token is copied from `.dev/ca.env` to `.dev/trial/secrets/ca_token` (mode 600) and reaches the container as `HOUND_CA_TOKEN_FILE`. It is never printed or put on a command line.

## 3. The workspace

`.dev/trial/` is git-ignored and holds everything the trial writes:

| Path | Holds |
| --- | --- |
| `repo/` | The organisation's compliance repo, as `hound init` writes it |
| `secrets/` | The API token and, later, the Sophos client secret (folder 700, files 600) |
| `manual/` | The manual source's drop folder |
| `evidence/` | Evidence and run records |
| `inbox/` | The example export the trial uploads |

## 4. A new organisation

`hound init /trial/repo` asks about the organisation: its name, a short code for ref IDs, the CISO Assistant address, the time zone, the Essential Eight maturity level, the audit's due date and the compliance owner. Press Enter to take each default, the fictional Example Pty Ltd at maturity level 2. That is the organisation the dev container seeds, so the plan updates its domain and perimeter and adds a new audit; give another name to keep them apart. It writes the config, the scope (the domain, the health control and the audit) and the Essential Eight mapping, and checks them. The trial then runs:

```bash
hound config validate hound.yaml
hound scope validate scope --config hound.yaml
hound scope plan scope --config hound.yaml
hound scope apply scope --config hound.yaml
```

Read the plan before you answer: it lists every object and field the apply would create or change. A second run plans nothing new.

## 5. Manual evidence and controls

Some evidence has no API. Here a system owner sends the GRC team a restore test record every quarter. The trial walks you through three wizards, suggesting answers for each:

1. `hound add system --scope scope --audit <audit>`: the system that holds the backups (Example backup service), added to the domain and the audit.
2. `hound add control --scope scope --audit <audit>`: the control the team relies on (AC-BACKUP, daily backups with quarterly restore tests), linked to the audit's backup requirements ISM-1511 and ISM-1515.
3. `hound add collector manual --config hound.yaml`: the manual source `example-backup-restore-test`, for the `backup.restore_tested` check. You give how often fresh evidence is due and who owns it.

It plans and applies the scope again, so the system and the control reach CISO Assistant, and writes an example restore test record to `inbox/`. You then:

1. **Upload and attest** it with `hound manual upload --config hound.yaml --source example-backup-restore-test …`. Answer `y`, your name, `pass` and a note.
2. **Collect and publish**: `hound run --config hound.yaml --only example-backup-restore-test`, then `hound status --config hound.yaml`.
3. **Look in CISO Assistant** at `https://localhost:8443`, signed in as `admin@example.com`:
   - The audit, requirement ISM-1515: its result, an observation naming who attested, and the uploaded evidence.
   - The applied controls AC-BACKUP and AC-AUTO-EVIDENCE, the health control, which links each run's health evidence.
   - The asset Example backup service.

## 6. Your Sophos Central tenant

`hound add collector sophos --config hound.yaml --secrets-dir /trial/secrets` asks for what the Sophos collector needs:

1. The credential's client ID, and the firewall firmware versions still in support (see Sophos's firmware lifecycle), such as `21.5, 22.0`. Other settings, such as `stale_agent_days` or a partner credential's `tenant_id`, are optional; add them with `--set key=value`.
2. The client secret, which is not shown as you type. It is saved to `.dev/trial/secrets/sophos_client_secret` (mode 600), never in git, and the config references it as `file:/trial/secrets/sophos_client_secret`.

The trial then runs:

```bash
hound config validate hound.yaml --check-secrets
hound run --config hound.yaml --only sophos --dry-run
hound run --config hound.yaml --only sophos
hound status --config hound.yaml
```

The dry run signs in, reads endpoints and firewalls, stores the evidence locally and plans the updates, and sends nothing. The real run publishes the four Sophos checks to the requirements they map to in the Essential Eight audit.

If you share output for help, remove identifiers first: tenant IDs, host names, IP addresses and serial numbers.

## Unattended

`HOUND_TRIAL_YES=1 make trial` answers yes to every question and gives the wizards their answers as options (`--yes`). It stops before the Sophos step, which needs your credential. CI uses it to check the trial against a fresh CISO Assistant.
