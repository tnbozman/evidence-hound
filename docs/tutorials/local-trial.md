# Local trial: Evidence Hound next to your CISO Assistant

This tutorial is interactive: `make trial` runs it step by step from the dev container. It takes Evidence Hound from nothing to results in CISO Assistant with `hound`'s own wizards ([guided setup](../guided-setup.md)): a new organisation (the fictional Example Pty Ltd, if you press Enter through the questions), a system, a control and a manual evidence source, then your own Sophos Central tenant, and optionally a Microsoft 365 test tenant. You need no server. Allow an hour, most of it the first image build.

```bash
make trial
```

Each step shows the `hound` command before it runs and asks before anything that changes CISO Assistant. Answer `q` to stop. Run `make trial` again to carry on: finished steps are skipped. `make trial-clean` removes the workspace and leaves CISO Assistant running.

## What it needs

- The dev container, with Docker answering inside it.
- A CISO Assistant. The dev container starts its own, at `http://backend:8000` in the dev container and `https://localhost:8443` in your browser. A stack from `make ca-up` works too. If none is running, the trial offers to start one.
- For the Sophos step: a Sophos Central API credential with the **Service Principal Read-Only** role, created as [the Sophos guide](../collectors/sophos.md) describes.
- For the optional Microsoft 365 step: a test tenant ([section 7](#7-a-microsoft-365-test-tenant)).

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
| `profiles/` | The `sophos-central` profile and its tests, copied from the repository for the Sophos step |
| `manual/` | The manual source's drop folder |
| `evidence/` | Evidence and run records |
| `inbox/` | The example export the trial uploads |

## 4. A new organisation

`hound init /trial/repo` asks about the organisation: its name, a short code for ref IDs, the CISO Assistant address, the time zone, the Essential Eight maturity level, the audit's due date and the compliance owner. Press Enter to take each default, the fictional Example Pty Ltd at maturity level 2. That is the organisation the dev container seeds, so the plan updates its domain and perimeter and adds a new audit; give another name to keep them apart. It writes the config, the scope (the domain, the health control and the audit) and the Essential Eight mapping, and checks them. The trial then runs:

```bash
hound config validate hound.yaml
hound scope validate scope --config hound.yaml
hound scope plan scope --config hound.yaml --out /trial/scope-plan.json
hound scope apply /trial/scope-plan.json
```

Read the plan before you answer: it lists every object and field the apply would create or change. The plan is saved outside the repo, and apply writes exactly that saved plan. If the scope or CISO Assistant changed in between, it refuses with `scope.plan_stale` ([Saved plans](../scope.md#saved-plans)). A second run plans nothing new.

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

The trial copies the `sophos-central` library profile and its tests into `.dev/trial/profiles`, then `hound add collector rest --name sophos --config hound.yaml --schedule "0 4 * * *" --secrets-dir /trial/secrets --set profile=sophos-central --set profiles_path=/trial/profiles` asks for what the profile needs:

1. The credential's client ID, and the firewall firmware versions still in support (see Sophos's firmware lifecycle), such as `21.5, 22.0`. Other settings, such as `super_admin_max`, keep their defaults; add them with `--set key=value`. The credential must be the tenant's own: partner and organisation credentials are not supported.
2. The client secret, which is not shown as you type. It is saved to `.dev/trial/secrets/sophos_client_secret` (mode 600), never in git, and the config references it as `file:/trial/secrets/sophos_client_secret`.

The trial then runs:

```bash
hound config validate hound.yaml --check-secrets
hound run --config hound.yaml --only sophos --dry-run
hound run --config hound.yaml --only sophos
hound status --config hound.yaml
```

The dry run signs in, reads the firewalls, stores the evidence locally and plans the updates, and sends nothing. The real run publishes the firewall firmware check to the requirement it maps to in the Essential Eight audit (ISM-1501, together with the Microsoft 365 collector's operating system check). Until that check has a result too, ISM-1501 stays `not_assessed`: step 7 adds it. Sophos Central is read for network devices only; evidence about endpoint protection comes from the endpoints' own antivirus, not from Sophos.

If you share output for help, remove identifiers first: tenant IDs, host names, IP addresses and serial numbers.

## 7. A Microsoft 365 test tenant

Use a test tenant, never a work one: the collector only reads, but a trial is the place to make mistakes. Two ways to get one:

- **The Microsoft 365 Developer Program**, if you qualify for its E5 sandbox (Microsoft limits who does). It has Entra ID P2 and Intune, so every check can run.
- **A Microsoft 365 Business Premium trial** (one month). It has Entra ID P1 and Intune, which the Conditional Access and device checks need; the PIM check reports `not_applicable` without P2. Cancel the trial when you are done.

In the tenant, register the app as [the Microsoft 365 guide](../collectors/m365.md#1-register-the-app) says: the six read-only Graph application permissions, admin consent, and a certificate. The trial offers to make a test certificate with `openssl` in `.dev/trial/inbox/` (valid 90 days): upload `hound-m365.crt` to the app, and the trial keeps `hound-m365.pem` (certificate and key). If you have your own, put it at `.dev/trial/inbox/hound-m365.pem` first.

Then `hound add collector m365 --config hound.yaml --secrets-dir /trial/secrets --secret-file client_certificate=/trial/inbox/hound-m365.pem` asks for the tenant ID, the app's client ID and the Windows builds still in support, such as `10.0.22631, 10.0.26100`. The other settings keep their defaults; add any with `--set key=value`. The PEM is copied to `.dev/trial/secrets/m365_client_certificate` (mode 600) and the inbox copy is removed. As with Sophos, the trial then validates the secrets, runs a dry run and, after asking, a real run and `hound status`.

If you share output for help, remove identifiers first: tenant and client IDs, domains, user names and device names.

## Unattended

`HOUND_TRIAL_YES=1 make trial` answers yes to every question and gives the wizards their answers as options (`--yes`). It stops before the Sophos and Microsoft 365 steps, which need your credentials. CI uses it to check the trial against a fresh CISO Assistant.
