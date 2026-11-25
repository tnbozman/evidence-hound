# Harden the host

The reference stack ([deploy/compose](../../deploy/compose/README.md)) is locked down inside Docker: only Caddy publishes a port, the
database network is internal, secrets are files, and the runner is read-only with no capabilities. This guide covers the host around
it. Work through it once when you build the VM, record the date in your private compliance repo, and check it again every year or
after a rebuild. Each section says what to do and how to check it.

The commands are for Ubuntu Server 24.04 LTS. Another distribution needs its own equivalents.

## 1. Patch the host

Install security updates every day, automatically, and reboot when a kernel update needs it.

```bash
sudo apt install unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
echo 'Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-Time "03:30";' | sudo tee /etc/apt/apt.conf.d/52evidence-hound
```

Pick a reboot time outside the backup (01:30) and the collectors' schedules. The stack's `restart: unless-stopped` brings it back.

**Check:** `sudo unattended-upgrade --dry-run --debug` lists the security origin; `/var/log/unattended-upgrades/` has today's log.

## 2. Firewall

Allow HTTPS for people and the agent's read-only account, and SSH only from your admin network. Deny everything else inbound.

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow from 192.0.2.0/24 to any port 22 proto tcp   # your admin network
sudo ufw allow 443/tcp                                       # or CA_HTTPS_PORT
sudo ufw enable
```

Docker publishes ports through its own iptables rules, which bypass `ufw`. Only Caddy publishes a port in the reference stack, so this
is safe as long as you add no other `ports:` in an override. If CISO Assistant should be reachable only inside your network, put the VM
behind your network firewall or a VPN instead of opening 443 to the internet.

**Check:** from another machine, `nmap -Pn <host>` shows 443 open and 22 only from the admin network.

## 3. Encrypt the disks

CISO Assistant holds your audit results, evidence and supplier records: sensitive data, and for a defence supplier possibly CUI. Keep it
encrypted at rest.

- On a cloud VM, turn on the provider's disk encryption with a key you control (a customer-managed key), for the boot and data volumes.
- On your own hardware, install Ubuntu with full-disk encryption (LUKS) and keep the recovery key in your password manager.
- Backups are encrypted by `backup.sh` with age ([backups](backups.md)); keep the private key off the host.

**Check:** `lsblk -o NAME,TYPE,FSTYPE,MOUNTPOINT` shows a `crypt` device under `/` (own hardware), or the provider's console shows the
volumes encrypted with your key.

## 4. Docker daemon

Rotate container logs, keep containers running through a daemon restart, and stop containers gaining privileges. In
`/etc/docker/daemon.json`:

```json
{
  "log-driver": "local",
  "log-opts": { "max-size": "20m", "max-file": "5" },
  "live-restore": true,
  "no-new-privileges": true,
  "userland-proxy": false
}
```

Then `sudo systemctl restart docker`. Only administrators are in the `docker` group: membership is root on the host.

**Check:** `docker info --format '{{.LoggingDriver}} {{.LiveRestoreEnabled}}'` prints `local true`; `getent group docker` lists only
administrators.

## 5. Forward the logs

Keep logs off the host, so an attacker on the VM cannot erase them and you can see the history after a failure.

- Send the host's journal (SSH logins, sudo, Docker) to your log platform or SIEM with its agent, or with `rsyslog` to a central
  syslog server.
- Send the containers' logs: `docker compose logs` covers CISO Assistant and the runner. With the `local` log driver, the platform's
  Docker integration reads them; or switch the driver to `journald` and forward the journal.
- Keep them at least as long as your framework asks (the ISM asks for event logs to be retained for at least seven years for some
  systems; agree the period with your authorising official).

**Check:** sign in over SSH, then find that login in the central log within a few minutes.

## 6. Certificates and uptime

Know before people do when CISO Assistant is down or its certificate is about to expire.

- Point an uptime monitor (your monitoring platform, or a hosted one) at `https://<CA_HOSTNAME>/api/health/` every five minutes, with an
  alert to the GRC team and IT.
- With Caddy's internal certificate authority, Caddy renews the site certificate itself; the root it issued is valid for ten years. With
  your own certificate, add a certificate-expiry check that alerts 30 days ahead.
- `hound status` shows each collector's last run; the collectors' health control in CISO Assistant shows the same to the GRC team.

**Check:** stop the frontend (`docker compose stop frontend`) and see the alert arrive; start it again.

## 7. MFA for every CISO Assistant user

Every person who signs in to CISO Assistant uses multi-factor authentication. In CISO Assistant, each user turns on MFA (an
authenticator app) under their profile's security settings; administrators check it for every account. If your CISO Assistant signs
people in through your identity provider (SSO), enforce MFA there with a Conditional Access or equivalent policy.

Service accounts (the runner, an agent's read-only account) sign in with personal access tokens, not passwords, so MFA does not apply;
section 8 covers them.

**Check:** the users list in CISO Assistant, or your identity provider's MFA report, shows every person enrolled.

## 8. A service account per integration

Give each integration its own account and token, with only the access it needs, so one leaked token can be revoked alone:

| Integration | Account | Access |
| --- | --- | --- |
| The runner | `hound-runner@...`, made by `runner-token.sh` | Writes audits and evidence |
| Your agent, for reports and questions | A Reader user on the folders in scope ([AI-assisted reporting](../govern/ai-assisted-reporting.md)) | Reads only |
| Each collector's source system | The collector's own app registration or API key ([collectors](../collectors/m365.md)) | Read-only roles listed per collector |

Tokens expire: `runner-token.sh` issues one for 365 days by default. Put the renewal in your evidence plan, and rotate a token at once
when someone who could read it leaves.

**Check:** CISO Assistant's users list has one service account per integration and no shared accounts; every token has an expiry.

## Record it

When you have worked through the guide, record the date, the VM and who did it in your private compliance repo (for example in
`deploy/hardening-record.md`), and attach it as evidence to your system hardening control. Repeat the checks every year.
