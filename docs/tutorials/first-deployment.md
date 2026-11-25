# First deployment: your private compliance repo and a scheduled run

This tutorial goes from an empty VM and an empty private repository to collectors running on a schedule and publishing to an Essential Eight audit in CISO Assistant. It uses the fictional Example Pty Ltd throughout; replace its names with yours as you go. Allow an afternoon, most of it waiting for CISO Assistant's first start and for approvals.

You need:

- A VM with Docker Engine and the Compose plugin, 4 GB of memory or more, and a DNS name for it (here `ciso.example.com`).
- A GitHub account or organisation where you can create private repositories.
- For the Microsoft 365 collector, rights to register an app in your Microsoft Entra tenant.

Two repositories are involved. The public Evidence Hound repo provides the stack ([`deploy/compose`](../../deploy/compose/README.md)) and the `hound` runner image. Your **private** compliance repo holds everything about your organisation: deployment settings, collector schedules, scope, access and settings. Secrets are in neither.

## 1. Create the private compliance repo

1. Copy the template, [`examples/compliance-repo`](../../examples/compliance-repo/README.md), into a new folder and make it a repository:

   ```bash
   git clone https://github.com/tnbozman/evidence-hound.git
   cp -r evidence-hound/examples/compliance-repo ~/compliance
   cd ~/compliance
   git init -b main && git add . && git commit -m "Start from the Evidence Hound template"
   gh repo create <owner>/compliance --private --source . --push
   ```

2. In the new repository's settings:
   - Turn on branch protection for `main`: require a pull request with at least one approving review, require the `validate` and `secrets` checks to pass, and block force pushes.
   - **Secret scanning** with **push protection** turned on (Settings > Code security), so a token pushed by mistake is blocked before it lands.
   - An **environment** named `ciso-assistant` with required reviewers, so every apply after a merge waits for a second approval.
3. Replace Example Pty Ltd with your organisation in `scope/setup.yaml`, `scope/controls.yaml`, `scope/access.yaml` and `hound.yaml`. Each YAML file's first line links a schema with a relative path into the Evidence Hound repo; point it at the schema of the Evidence Hound version you run so your editor keeps checking the files.

To start from a smaller repo instead, `hound init` asks about your organisation and writes the config, scope, audit and mapping, and `hound add` adds systems, controls and collectors ([guided setup](../guided-setup.md)). Copy the template's `deploy/`, `scope/settings.yaml` and `.github/` into it. Keep the `scope/access.yaml` that `hound init` wrote: its GRC team is the one the evidence plan names, and the template's would replace it.

Commit each step on a branch and merge it through a pull request, so the repo's history starts as it will continue.

## 2. What goes in the repo, and what never does

| In the repo | Never in the repo |
| --- | --- |
| `deploy/deploy.env`: the CISO Assistant release (`CA_VERSION`), host name, time zone, runner image tag and where things are on the host | Passwords, including the Postgres password and Django secret key |
| `deploy/compose.override.yml`: your additions to the stack | API tokens, such as the runner's CISO Assistant token |
| `hound.yaml`: collectors, their schedules and the target audit | Certificates and private keys, such as the Microsoft 365 app's |
| `scope/`: `scope/setup.yaml`, `scope/controls.yaml`, `scope/audits/`, `scope/access.yaml` and `scope/settings.yaml` | Exported evidence, logs and screenshots |
| `mappings/`, `frameworks/`, `documents/` (policies and procedures) and `manual-sources/README.md` | `.env` files |

Secrets live on the VM as Docker secrets files in `/etc/evidence-hound/secrets` (or in a secret manager) and are named from the repo by reference only: `file:/run/secrets/m365_client_certificate` or `env:HOUND_CA_TOKEN`. The template's `.gitignore` excludes secrets, keys, certificates, `.env` files and evidence, and its workflow runs gitleaks on every change as a second line behind push protection.

## 3. Install the stack on the VM

1. Make the secrets folder and the two secrets CISO Assistant needs. The runner's token and the Microsoft 365 certificate start empty and are filled below.

   ```bash
   sudo install -d -m 0700 /etc/evidence-hound/secrets
   cd /etc/evidence-hound/secrets
   sudo sh -c 'umask 022; openssl rand -hex 24 > postgres_password; openssl rand -hex 32 > django_secret_key; : > hound_ca_token; : > m365_client_certificate'
   ```

2. Check out both repositories. Give the VM read-only access to your private repo with a deploy key.

   ```bash
   sudo git clone https://github.com/tnbozman/evidence-hound.git /opt/evidence-hound
   sudo git clone git@github.com:<owner>/compliance.git /opt/compliance
   ```

3. Link your settings as the stack's `.env`. `deploy/deploy.env` sets `COMPOSE_FILE`, so every `docker compose` command in `deploy/compose`, and the stack's own scripts, also use your `deploy/compose.override.yml`.

   ```bash
   sudo ln -s /opt/compliance/deploy/deploy.env /opt/evidence-hound/deploy/compose/.env
   cd /opt/evidence-hound/deploy/compose
   docker compose config --quiet
   docker compose up -d --wait postgres backend worker frontend caddy
   ```

   The first start migrates the database and loads CISO Assistant's libraries, which takes several minutes.

4. Create the first administrator, who signs in to CISO Assistant as a person:

   ```bash
   ./manage.sh createsuperuser
   ```

5. Sign in at `https://ciso.example.com`. Caddy signs the certificate with its own authority; install its root on your devices (see the [stack's guide](../../deploy/compose/README.md#install)) or use your own certificate.
6. Create the runner's service account and its token. The script writes the token straight into the secrets folder; it is never shown or copied.

   ```bash
   sudo ./runner-token.sh hound-runner@example.com /etc/evidence-hound/secrets
   ```

   `runner-token.sh` puts the account in the administrators group, so the first apply below can run with its token. Once the CI workflow applies with its own account, declare the runner in `scope/access.yaml` with `service_account: publish` and the analyst role on the domains it publishes to. The next apply narrows its groups to that, and `scope plan`'s access review stops flagging it.

## 4. Register the Microsoft 365 app

Follow the [Microsoft 365 collector guide](../collectors/m365.md) (`docs/collectors/m365.md`): register the app, grant its read-only Graph permissions, and create its certificate on the VM. Store the PEM (certificate followed by its private key) as the `m365_client_certificate` secret, then delete the loose key file:

```bash
sudo install -m 0444 hound-m365.pem /etc/evidence-hound/secrets/m365_client_certificate
sudo rm hound-m365.pem hound-m365-private.pem
```

Put the tenant and client IDs (not secrets) in `hound.yaml` through a pull request, merge it, and pull it on the VM with `git -C /opt/compliance pull`. Check the config and that every secret resolves:

```bash
docker compose run --rm runner config validate /config/hound.yaml --check-secrets
```

## 5. Validate, plan and apply the scope

Do this in two pull requests, so the first apply is small and easy to check.

**First: setup, settings and access.** Move the audit files out of `scope/audits/` for now and keep `scope/setup.yaml`, `scope/controls.yaml`, `scope/access.yaml` and `scope/settings.yaml`. The template's settings turn on `enforce_mfa`, so everyone must set up multi-factor authentication at their next sign-in, and leave the modules you do not use off. On your own machine, run `hound` from the runner image against your checkout:

```bash
hound() { docker run --rm -v "$PWD:/config:ro" -w /config ghcr.io/tnbozman/evidence-hound:1.0.0 "$@"; }
hound config validate hound.yaml
hound scope validate scope/ --config hound.yaml
```

Validation runs offline and reports every problem with its file, line, code and a hint. Then, on the VM, see what applying would do, and apply:

```bash
docker compose run --rm runner scope plan /config/scope --config /config/hound.yaml
docker compose run --rm runner scope apply /config/scope --config /config/hound.yaml
```

The plan lists every user (with the roles they will hold), team, flag and setting it will create or change. New users get CISO Assistant's invitation and set their own password; `hound` never handles one. The plan's access review lists anyone who has not turned on MFA yet.

**Second: the Essential Eight audit.** Put `scope/audits/ex-e8-2026.yaml` back (renamed for your audit; a repo from `hound init` has `scope/audits/<code>-e8.yaml` instead, and no `scope/settings.yaml` until you copy the template's), check the mapping, and repeat validate, plan and apply:

```bash
hound mapping validate mappings/essential-eight-2023.yaml
hound scope validate scope/ --config hound.yaml
```

After apply, the audit, its controls and documents are in CISO Assistant, and a second `scope plan` lists no changes.

## 6. Turn on the schedule and check the first run

Run the collectors once by hand, then start the runner, which runs each collector on its `schedule` in `scheduler.time_zone`:

```bash
docker compose run --rm runner run --config /config/hound.yaml
docker compose run --rm runner status --config /config/hound.yaml
docker compose up -d runner
docker compose logs runner
```

`status` shows each collector's latest run and the result it published; the logs show each collector's next run. In CISO Assistant, open the audit: requirements the collectors cover have results and evidence, and the `AC-AUTO-EVIDENCE` control shows that collection is healthy. Manual sources show as due until their owners drop an export into the folders `manual-sources/README.md` lists.

## 7. Day-2 changes

From now on, every change goes in as a pull request to the compliance repo, whoever makes it:

- **Access**: a new starter, a leaver (removed from `scope/access.yaml`, then deactivated by `scope apply --prune`, never deleted) or a role change.
- **Settings**: a feature flag or general setting in `scope/settings.yaml`.
- **Scope**: systems, controls, audits, documents and sign-offs.
- **Schedules and collectors**: `hound.yaml`.
- **Upgrades**: `CA_VERSION` or `HOUND_RUNNER_IMAGE` in `deploy/deploy.env`; snapshot the VM first.

The repo's workflow, [`.github/workflows/compliance.yml`](../../examples/compliance-repo/.github/workflows/compliance.yml), scans the change for secrets, validates it, comments the `scope plan` on the pull request so reviewers approve the change in CISO Assistant's terms, and applies it after merge once the `ciso-assistant` environment's reviewers approve. It needs a self-hosted runner that can reach CISO Assistant and the `HOUND_CA_URL` and `HOUND_CA_TOKEN` repository secrets. After a merge that changes `deploy/` or `hound.yaml`, pull it on the VM and restart: `git -C /opt/compliance pull && docker compose up -d`.

The [scope files guide](../scope.md) covers every file in detail, and [running collectors](../running.md) covers the schedule, status and health.
