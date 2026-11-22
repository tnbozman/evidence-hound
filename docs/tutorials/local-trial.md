# Local trial: Evidence Hound next to your CISO Assistant

This tutorial is interactive: `make trial` runs it step by step from the dev container. It takes Evidence Hound from nothing to results in CISO Assistant, using the fictional Example Pty Ltd, and then adds your own Sophos Central tenant. You need no server and no Microsoft 365 tenant. Allow an hour, most of it the first image build.

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
| `repo/` | A copy of the [example compliance repo](../../examples/compliance-repo/README.md), with a trial `hound.yaml`: the manual collectors and the Essential Eight target `EX-E8-2026`, and no Microsoft 365 |
| `secrets/` | The API token and, later, the Sophos client secret (folder 700, files 600) |
| `manual/` | The manual collectors' drop folders |
| `evidence/` | Evidence and run records |
| `inbox/` | The example export the trial uploads |

The trial checks the config with `hound config validate hound.yaml`.

## 4. The example organisation

The scope folder declares Example Pty Ltd as configuration: its domain, applied controls, documents and audits (see [scope](../scope.md)). The trial runs:

```bash
hound scope validate scope --config hound.yaml
hound scope plan scope --config hound.yaml
hound scope apply scope --config hound.yaml
```

Read the plan before you answer: it lists every object and field the apply would create or change. A second run plans nothing new.

## 5. Manual evidence and controls

Some evidence has no API. Here a system owner has sent the GRC team a restore test record, and the trial writes an example of one to `inbox/`. You then:

1. **Upload and attest** it with `hound manual upload --config hound.yaml --source example-backup-restore-test …`. Answer `y`, your name, `pass` and a note.
2. **Collect and publish**: `hound run --config hound.yaml --only example-backup-restore-test`, then `hound status --config hound.yaml`.
3. **Look in CISO Assistant** at `https://localhost:8443`, signed in as `admin@example.com`:
   - The Essential Eight ML2 2026 audit, requirement ISM-1515: its result, an observation naming who attested, and the uploaded evidence.
   - The applied controls AC-BACKUP (from `scope/controls.yaml`) and AC-AUTO-EVIDENCE, the health control, which links each run's health evidence.

## 6. Your Sophos Central tenant

1. Paste the client ID, then the client secret (it is not shown). Both are saved under `.dev/trial/secrets/` and `.dev/trial/state/`, never in git.
2. Give the firewall firmware versions still in support (see Sophos's firmware lifecycle) and the number of days an endpoint may go unseen (default 14).
3. The trial adds a `sophos` collector to `repo/hound.yaml`, with `client_secret: file:/trial/secrets/sophos_client_secret`, then runs:

   ```bash
   hound config validate hound.yaml --check-secrets
   hound run --config hound.yaml --only sophos --dry-run
   hound run --config hound.yaml --only sophos
   hound status --config hound.yaml
   ```

The dry run signs in, reads endpoints and firewalls, stores the evidence locally and plans the updates, and sends nothing. The real run publishes the four Sophos checks to the requirements they map to in the Essential Eight audit.

If you share output for help, remove identifiers first: tenant IDs, host names, IP addresses and serial numbers.

## Unattended

`HOUND_TRIAL_YES=1 make trial` answers yes to every question. It stops before the Sophos step, which needs your credential. CI uses it to check the trial against a fresh CISO Assistant.
