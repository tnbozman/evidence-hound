# Local trial: Evidence Hound next to your CISO Assistant

This tutorial is interactive: `make trial` runs it step by step from the dev container. It takes Evidence Hound from nothing to results in CISO Assistant in the order you onboard for real: the organisation with `hound init` ([guided setup](../guided-setup.md)) (the fictional Example Pty Ltd, if you press Enter through the questions), its third parties, the Sophos perimeter firewall as a system, the system's audit, the controls it relies on, and its evidence collectors: your own Sophos Central tenant and Microsoft 365, from a fictional tenant on this machine or your own test tenant. From the third parties on, your coding agent drafts each stage with hound's skills and you approve it. You need no server. Allow an hour, most of it the first image build.

```bash
make trial
```

Each step shows the `hound` command before it runs and asks before anything that changes CISO Assistant. Answer `q` to stop. Run `make trial` again to carry on: finished steps are skipped. `make trial-clean` removes the workspace and leaves CISO Assistant running.

## What it needs

- The dev container, with Docker answering inside it.
- A CISO Assistant. The dev container starts its own, at `http://backend:8000` in the dev container and `https://localhost:8443` in your browser. A stack from `make ca-up` works too. If none is running, the trial offers to start one.
- For the Sophos step: a Sophos Central API credential with the **Service Principal Read-Only** role, created as [the Sophos guide](../collectors/sophos.md) describes.
- For the Microsoft 365 step: nothing, with the dev server; or a test tenant of your own ([section 10](#10-evidence-collectors-microsoft-365)).

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
| `manual/` | Drop folders for manual evidence sources |
| `evidence/` | Evidence and run records |
| `specs/` | The firewall's API specification, for the agent |
| `inbox/` | Vendor documents for the agent, and the Microsoft 365 test certificate |
| `bin/hound` | `hound` for your coding agent: the trial's image, against the trial's CISO Assistant |

## 4. A new organisation

`hound init /trial/repo` asks about the organisation: its name, a short code for ref IDs, the CISO Assistant address, the time zone, the Essential Eight maturity level, whether the audit runs by quarter or by month, and the compliance owner. Press Enter to take each default, the fictional Example Pty Ltd at maturity level 2, audited by quarter: the series `EX-E8`, whose open period on 23 November 2026 is `EX-E8-Q2-2027FY`. It writes the config, the scope (the domain, the health control and the audit) and the Essential Eight mapping, and checks them. The trial then runs:

```bash
hound config validate hound.yaml
hound scope validate scope --config hound.yaml
hound scope plan scope --config hound.yaml --out /trial/scope-plan.json
hound scope apply /trial/scope-plan.json
```

Read the plan before you answer: it lists every object and field the apply would create or change. The plan is saved outside the repo, and apply writes exactly that saved plan. If the scope or CISO Assistant changed in between, it refuses with `scope.plan_stale` ([Saved plans](../scope.md#saved-plans)). A second run plans nothing new.

It then runs `hound agent setup --assistant both`, which writes the skills and agent files for Claude Code and GitHub Copilot ([onboarding](../onboarding.md)), and links `.dev/trial/bin/hound` to `scripts/trial-hound.sh`: it runs the trial's runner image against the trial's CISO Assistant with the trial's token, in the folder you call it from. hound's container sees only `.dev/trial`, so it refuses a folder outside it. Start your agent in a second terminal and keep it open:

```bash
cd .dev/trial/repo && export PATH="$PWD/../bin:$PATH"
claude
```

Or open `.dev/trial/repo` in VS Code and pick the `hound-onboarding` agent in Copilot. Put the firewall's API specification in `.dev/trial/specs` and the vendor documents in `.dev/trial/inbox`.

## 5 to 8. Third parties, the system, its audit and controls

Each of these stages works the same way. The trial says what the stage is and prints what to ask your agent, with the skills it uses. It waits until `hound` sees the draft in the repo; answer `y` when the agent has finished, or `q` to stop and carry on later. Where a person must approve, it runs `hound approve --repo .` in your terminal, which shows each draft one at a time (answer `y`, `n` or `s`). Then it validates, plans and applies, as in step 4, and says what to look at in CISO Assistant.

| Stage | The agent drafts (skills) | You | In CISO Assistant |
| --- | --- | --- | --- |
| 5. Third parties | Sophos, who makes the firewall, and the managed service provider that runs it (fictional Example Managed Services Pty Ltd, an `msp` for Sophos), with `hound supplier draft` (onboard-a-third-party, assess-a-supplier) | Approve each supplier and conclude its review with `hound approve` | Third parties SUP-SOPHOS and SUP-EXAMPLE-MSP, with their solutions, contracts and reviews |
| 6. The system | The session for `SOPHOS-NETWORK`, with `hound onboard SOPHOS-NETWORK pending`: owner, product, solution, maturity level and the API specification (onboard-a-system-with-hound) | Answer what it asks | Nothing yet: the session waits in `repo/onboarding/SOPHOS-NETWORK/` |
| 7. Its audit | One decision per requirement with `hound onboard SOPHOS-NETWORK decide` (read-an-api-specification), then the review (review-before-apply) | Approve the decisions with `hound approve`, which writes the system and its audit | The asset `SYS-SOPHOS-NETWORK` and the audit `EX-E8-<period>-SOPHOS-NETWORK`, such as `EX-E8-Q2-2027FY-SOPHOS-NETWORK`, next to `EX-E8-Q2-2027FY` |
| 8. Controls | The applied controls the audit's manual requirements rely on, with `hound add control … --audit EX-E8-SOPHOS-NETWORK` | Review them | The applied controls, linked to the audit's requirements |

A finished stage is skipped on the next run; remove its file in `.dev/trial/state/` to do it again.

## 9. Evidence collectors: your Sophos Central tenant

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

The dry run signs in, reads the firewalls, stores the evidence locally and plans the updates, and sends nothing. The real run publishes the firewall firmware check to the requirement it maps to in the Essential Eight audit (ISM-1501, together with the Microsoft 365 collector's operating system check). Until that check has a result too, ISM-1501 stays `not_assessed`: step 10 adds it. Sophos Central is read for network devices only; evidence about endpoint protection comes from the endpoints' own antivirus, not from Sophos.

If you share output for help, remove identifiers first: tenant IDs, host names, IP addresses and serial numbers.

## 10. Evidence collectors: Microsoft 365

The trial offers the [Microsoft 365 dev server](m365-dev-server.md) first: a fictional Example Pty Ltd tenant that answers Microsoft Graph on this machine, with no tenant, no sign-in and nothing leaving the machine. Choose the compliant tenant, which passes every check, or the gaps tenant, where each named check fails. The trial:

1. Starts it (`make m365-dev ARGS="--variant compliant"`), with its output in `.dev/trial/m365-dev.log`, and waits until it answers on `http://localhost:5365/`.
2. Adds the m365 collector with the settings the server prints, `graph_url: http://localhost:5365/` and `sign_in: none`, so it asks for no certificate or secret. `sign_in: none` is refused for any address not on this machine ([settings](../collectors/m365.md)).
3. Validates, dry-runs and, after asking, publishes, with hound's container on the host network so it reaches the server; then shows `hound status` and stops the server.

The checks publish to the open period of the organisation's audit, `EX-E8-Q2-2027FY` on 23 November 2026. A later `make trial` starts the server again and runs the same collector.

### Your own test tenant

Answer `n` to the dev server to use a tenant of your own instead.

Use a test tenant, never a work one: the collector only reads, but a trial is the place to make mistakes. Two ways to get one:

- **The Microsoft 365 Developer Program**, if you qualify for its E5 sandbox (Microsoft limits who does). It has Entra ID P2 and Intune, so every check can run.
- **A Microsoft 365 Business Premium trial** (one month). It has Entra ID P1 and Intune, which the Conditional Access and device checks need; the PIM check reports `not_applicable` without P2. Cancel the trial when you are done.

In the tenant, register the app as [the Microsoft 365 guide](../collectors/m365.md#1-register-the-app) says: the six read-only Graph application permissions, admin consent, and a certificate. The trial offers to make a test certificate with `openssl` in `.dev/trial/inbox/` (valid 90 days): upload `hound-m365.crt` to the app, and the trial keeps `hound-m365.pem` (certificate and key). If you have your own, put it at `.dev/trial/inbox/hound-m365.pem` first.

Then `hound add collector m365 --config hound.yaml --secrets-dir /trial/secrets --secret-file client_certificate=/trial/inbox/hound-m365.pem` asks for the tenant ID, the app's client ID and the Windows builds still in support, such as `10.0.22631, 10.0.26100`. The other settings keep their defaults; add any with `--set key=value`. The PEM is copied to `.dev/trial/secrets/m365_client_certificate` (mode 600) and the inbox copy is removed. As with Sophos, the trial then validates the secrets, runs a dry run and, after asking, a real run and `hound status`.

If you share output for help, remove identifiers first: tenant and client IDs, domains, user names and device names.

## Unattended

`HOUND_TRIAL_YES=1 make trial` answers yes to every question and gives the wizards their answers as options (`--yes`). Stages 5 to 8 replay fictional answers through the same commands an agent uses: the third parties with `hound supplier draft`, the system with `hound onboard SOPHOS-NETWORK pending`, its decisions from the onboarding tutorial's answers typed into `hound onboard`, and a control with `hound add control`. Because `hound approve` refuses without a person at a terminal, approved copies of the two supplier files stand in for it ([`examples/trial/`](../../examples/trial/README.md)). It stops before the Sophos step, which needs your credential, and runs the Microsoft 365 step against the dev server's compliant tenant. CI uses it to check the trial against a fresh CISO Assistant, twice: the second run applies nothing new.
