# Local trial: Evidence Hound next to your CISO Assistant

This tutorial is interactive: `make trial` runs it step by step from the dev container. It sets a scene and walks it the way you would onboard for real, so you can explore CISO Assistant and Evidence Hound with little fuss.

**The scene.** The fictional Example Pty Ltd self-hosts its cyber GRC: CISO Assistant and Evidence Hound run in Docker Compose on one private Oracle Cloud (OCI) VM with no public IP address (`GRC-PLATFORM`). The office reaches it only over a site-to-site VPN through its Sophos XGS 88W firewall, managed in Sophos Central (`OFFICE-FIREWALL`). Staff sign in with Microsoft 365. The GRC team audits each system to the Essential Eight, maturity level 2. This is [the onboarding tutorial's deployment](../examples/onboarding-tutorial.md#the-deployment), with its diagram.

**The order.** The organisation with `hound init` ([guided setup](../guided-setup.md)), its third parties (Oracle, Sophos, and the CISO Assistant and Evidence Hound projects), then each system with its own audit and the controls it relies on, and last the collectors that gather evidence on a schedule: the IaC pipeline's control tests and drift check for `GRC-PLATFORM` ([its dev server](iac-dev-server.md)), Sophos Central for `OFFICE-FIREWALL` ([its dev server](sophos-dev-server.md)) Microsoft 365 for identity and laptops ([its dev server](m365-dev-server.md)), and vulnerability scan exports for patching ([fictional exports](scan-dev.md)). Every collector reads a fictional tenant on your machine, so you need no account, API key or cloud; Sophos Central and Microsoft 365 can be your own tenants instead. From the third parties on, your coding agent drafts each stage with hound's skills and you approve it. Allow an hour, most of it the first image build.

```bash
make trial
```

Each step shows the `hound` command before it runs and asks before anything that changes CISO Assistant. Answer `q` to stop. Run `make trial` again to carry on: finished steps are skipped. `make trial-clean` removes the workspace and leaves CISO Assistant running.

## What it needs

- The dev container, with Docker answering inside it.
- A CISO Assistant. The dev container starts its own, at `http://backend:8000` in the dev container and `https://localhost:8443` in your browser. A stack from `make ca-up` works too. If none is running, the trial offers to start one.
- No account or key: each collector reads its dev server. Optionally Terraform (the dev container has it), so the trial runs the GRC platform's real control tests.
- For your own Sophos Central tenant instead (step 13), an API credential with the **Service Principal Read-Only** role, created as [the Sophos guide](../collectors/sophos.md) describes; for your own Microsoft 365 test tenant (step 14), [section 14](#14-identity-and-laptops-microsoft-365).

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
| `profiles/` | The profiles the collectors read, with their tests: `grc-infra-controls` and `terraform-drift` (step 12), `sophos-central` (step 13) |
| `iac/` | The GRC platform's Terraform, copied to run its control tests, and their report `controls.xml` (step 12) |
| `manual/` | Drop folders for manual evidence sources |
| `evidence/` | Evidence and run records |
| `specs/` | API specifications, for the agent |
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

Or open `.dev/trial/repo` in VS Code and pick the `hound-onboarding` agent in Copilot. Put any API specification in `.dev/trial/specs` and the vendor documents in `.dev/trial/inbox`.

## 5 to 11. Third parties, then each system, its audit and controls

Each of these stages works the same way. The trial says what the stage is and prints what to ask your agent, with the skills it uses. It waits until `hound` sees the draft in the repo; answer `y` when the agent has finished, or `q` to stop and carry on later. Where a person must approve, it runs `hound approve --repo .` in your terminal, which shows each draft one at a time (answer `y`, `n` or `s`). Then it validates, plans and applies, as in step 4, and says what to look at in CISO Assistant. Steps 6 to 8 are `GRC-PLATFORM`; steps 9 to 11 are the same for `OFFICE-FIREWALL`.

| Stage | The agent drafts (skills) | You | In CISO Assistant |
| --- | --- | --- | --- |
| 5. Third parties | Oracle (OCI hosting, `SOL-OCI-HOSTING`) and the CISO Assistant and Evidence Hound projects for `GRC-PLATFORM`; Sophos (the XGS 88W and Sophos Central, `SOL-SOPHOS-FIREWALL`) for `OFFICE-FIREWALL`, with `hound supplier draft` (onboard-a-third-party, assess-a-supplier) | Approve each supplier and conclude its review with `hound approve`. A system is registered only once its supplier's review is concluded ok | Third parties SUP-ORACLE, SUP-SOPHOS, SUP-CISO-ASSISTANT and SUP-EVIDENCE-HOUND, with their solutions, contracts and reviews |
| 6 and 9. The system | The session, with `hound onboard <system> pending`: owner, product, solution and maturity level (onboard-a-system-with-hound) | Answer what it asks | Nothing yet: the session waits in `repo/onboarding/<system>/` |
| 7 and 10. Its audit | One decision per requirement with `hound onboard <system> decide` (read-an-api-specification), then the review (review-before-apply) | Approve the decisions with `hound approve`, which writes the system and its audit | The asset and the audit `EX-E8-<period>-<system>`, such as `EX-E8-Q2-2027FY-GRC-PLATFORM`, next to the organisation's `EX-E8-Q2-2027FY` |
| 8 and 11. Controls | The applied controls the audit's manual requirements rely on, with `hound add control … --audit EX-E8-<system>`: for `GRC-PLATFORM` patching (ISM-1695) and restore tests (ISM-1515), for `OFFICE-FIREWALL` a daily external scan (ISM-1701) | Review them | The applied controls, linked to the audit's requirements |

A finished stage is skipped on the next run; remove its file in `.dev/trial/state/` to do it again.

## 12. GRC-PLATFORM's evidence: its hosting controls and drift, from the IaC pipeline

The GRC platform's network, backups and hosting controls are Terraform ([`infra/oci`](../../examples/onboarding-tutorial/infra/oci)). Its IaC pipeline proves each control with `terraform test` on every change and checks nightly that the cloud still matches the code ([the tutorial, steps 5 and 6](../examples/onboarding-tutorial.md#5-check-the-platform-for-drift)). hound only reads the pipeline's reports through the GitHub Actions API; it never runs Terraform against the cloud and holds no cloud credential. In the trial:

1. If Terraform is installed, the trial copies the Terraform to `.dev/trial/iac/oci` and runs `terraform init -backend=false` and `terraform test -junit-xml=controls.xml`, as the pipeline would. The tests use mock providers: no Oracle account, nothing created.
2. It starts [the IaC pipeline dev server](iac-dev-server.md) (`make iac-dev` on port 5380), which serves that report, or its own report of the same tests without Terraform, and a drift plan with no drift, as the fictional repository `example/grc-infra`.
3. It copies the tutorial's mapping for the system, `mappings/grc-platform-essential-eight-2023.yaml`, and adds a target for `EX-E8-GRC-PLATFORM` to `hound.yaml`, so the results reach the system's own audit. No hound command writes these yet.
4. It adds two rest collectors with a placeholder token, `grc-infra-controls` (the tutorial's per-control copy of `terraform-tests`) and `grc-infra-drift` (`terraform-drift`), then validates, dry-runs and, after asking, publishes each.

Each backup row (ISM-1811, ISM-1705, ISM-1812, ISM-1707, ISM-1814) needs its control test **and** no drift: the test proves the code declares the control, and the drift check proves the cloud still matches the code. The trial then offers to restart the dev server with the `drift` deviation and collect again: the backup rows now fail. The system's other rows stay manual, with the controls from step 8 and it@example.com as their owner.

## 13. OFFICE-FIREWALL's evidence: Sophos Central, a fictional tenant on this machine or your own

The trial copies the `sophos-central` library profile and its tests into `.dev/trial/profiles`, then asks which tenant to read. Press Enter for the dev server; an unattended run takes it too.

### The dev server (default)

The trial starts [the Sophos Central dev server](sophos-dev-server.md) in the background (`make sophos-dev` on port 5370, log in `.dev/trial/sophos-dev.log`, stopped when the trial ends). It serves the fictional Example Pty Ltd tenant: two XGS firewalls on supported firmware and five admins, two of them Super Admins. Nothing leaves the machine and no question asks for a credential:

```bash
hound add collector rest --name sophos --config hound.yaml --schedule "0 4 * * *" --secrets-dir /trial/secrets \
  --set profile=sophos-central --set profiles_path=/trial/profiles \
  --set client_id=00000000-0000-4000-8000-0000050d0001 \
  --set id_url=http://localhost:5370/ --set api_url=http://localhost:5370/ \
  --set "supported_firewall_firmware=21.0, 21.5" \
  --secret-file client_secret=/trial/inbox/sophos-dev-secret --yes
```

The secret is a placeholder the trial writes; the dev server checks no credential. On a `make ca-up` network the address is `http://host.docker.internal:5370/` instead, because hound runs in its own container there.

### Your own tenant

Answer `n` and `hound add collector rest --name sophos --config hound.yaml --schedule "0 4 * * *" --secrets-dir /trial/secrets --set profile=sophos-central --set profiles_path=/trial/profiles` asks for what the profile needs:

1. The credential's client ID, and the firewall firmware versions still in support (see Sophos's firmware lifecycle), such as `21.5, 22.0`. Other settings, such as `super_admin_max`, keep their defaults; add them with `--set key=value`. The credential must be the tenant's own: partner and organisation credentials are not supported.
2. The client secret, which is not shown as you type. It is saved to `.dev/trial/secrets/sophos_client_secret` (mode 600), never in git, and the config references it as `file:/trial/secrets/sophos_client_secret`.

### Then, either way

The trial runs:

```bash
hound config validate hound.yaml --check-secrets
hound run --config hound.yaml --only sophos --dry-run
hound run --config hound.yaml --only sophos
hound status --config hound.yaml
```

Before it runs, the trial copies the tutorial's `mappings/office-firewall-essential-eight-2023.yaml` and targets `EX-E8-OFFICE-FIREWALL`, as in step 12. The dry run signs in, reads the firewalls, stores the evidence locally and plans the updates, and sends nothing. The real run publishes the firmware and Super Admin checks to `OFFICE-FIREWALL`'s audit (ISM-1501 and ISM-1883) and the firmware check to the organisation's (ISM-1501, together with the Microsoft 365 collector's operating system check, so that row waits for step 14). Sophos Central is read for network devices only; evidence about endpoint protection comes from the endpoints' own antivirus, not from Sophos.

With the dev server, the trial then offers to restart it with the `unsupported-firmware` deviation (the branch firewall on SFOS 20.0.3) and run again: the firmware check now fails, and you can watch the result change in CISO Assistant. This is continual evidence collection in miniature: the schedule runs the same collector every day and CISO Assistant keeps each result.

If you share output for help, remove identifiers first: tenant IDs, host names, IP addresses and serial numbers.

## 14. Identity and laptops: Microsoft 365

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

## 15. Patching evidence: vulnerability scans

Scanners export files instead of answering an API, so the `vuln-scan` collector reads exports dropped in a folder ([its guide](../collectors/vuln-scan.md)). The trial:

1. Writes [fictional scan exports](scan-dev.md) for the Example Pty Ltd estate into `.dev/trial/scans`, dated from today. That is `make scan-dev ARGS="--variant compliant --out ../scans"`, run from the workspace's `repo` folder, with its output in `.dev/trial/scan-dev.log`. There is one export per scope: Nessus for internet-facing services, other applications and operating systems, and a Defender CSV for the laptops.
2. Adds one `vuln-scan` source per scope, named `internet-facing-scans`, `laptops-scans`, `other-apps-scans` and `operating-systems-scans`, with the settings the generator printed. Their drop folders are `../scans/<scope folder>`, relative to `hound.yaml`.
3. Runs each source dry, then publishes after asking. The eleven patching checks reach the Essential Eight rows they map to: scanning frequency, patch windows and unsupported software.
4. Offers to write the gaps estate and collect again. That estate has a stale internet-facing scan, an exploited web flaw past 48 hours, an unsupported VPN appliance, an overdue browser patch, an overdue operating system update and an unsupported laptop operating system. The patching rows then fail, naming the hosts.

In a real deployment, each new export goes into the same folder, by hand or with `hound manual upload --source <name>`.

## Unattended

`HOUND_TRIAL_YES=1 make trial` answers yes to every question and gives the wizards their answers as options (`--yes`). Stages 5 to 11 replay fictional answers through the same commands an agent uses: the four third parties with `hound supplier draft`, each system with `hound onboard <system> pending`, its decisions from the onboarding tutorial's answers ([`answers/`](../../examples/onboarding-tutorial/answers)) typed into `hound onboard`, and its controls with `hound add control`. Because `hound approve` refuses without a person at a terminal, approved copies of the four supplier files stand in for it ([`examples/trial/`](../../examples/trial/README.md)). Steps 12 to 15 use the three dev servers and the fictional scan exports, publish, and collect again with the `drift` and `unsupported-firmware` deviations and the gaps scan estate. No step needs a credential. CI uses it to check the trial against a fresh CISO Assistant, twice: the second run applies nothing new.
