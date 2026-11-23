# Hosting controls proven by your infrastructure tests

The `terraform-tests` profile turns the tests in your infrastructure code into evidence. You write each hosting control as a
`terraform test` run block, for example "the VM has no public IP address". Your IaC pipeline runs the tests on every change and
publishes the JUnit report as a build artefact. `hound` reads the artefact through the GitHub Actions API. It is an ordinary
[REST profile](rest.md): `hound` never runs Terraform, never reads state and never holds cloud credentials.

The tests use **mock providers** (`mock_provider`, Terraform 1.7 and later). Terraform plans your real code against a fake provider,
so the tests need no cloud account and no credentials, create nothing and cost nothing. The same works for any provider: OCI, AWS,
Azure or Google Cloud.

**What a mock proves, and what it does not.** A control test proves that the reviewed code declares the control. It does not prove
that the live cloud looks like that, because someone could change the cloud by hand. The [drift check](terraform-drift.md) covers
that: it proves the live cloud still matches the code. Map a requirement to both (`rule: all_pass`), and it passes only when the code
declares the control and the cloud still matches the code.

| Profile | What it does | Token |
| --- | --- | --- |
| `terraform-tests` | Reads the newest completed run of the tests workflow, its artefact and the JUnit report in it. Read only. | Fine-grained, on the IaC repository only, **Actions: read**. The drift token will do. |

Its check:

- `infrastructure.tests.pass`: **pass** when every test in the report passed; **fail** when one failed or ended in error, naming
  each test with Terraform's message (the assertion's `error_message`); **unable to assess** when there is no completed run, the run
  was cancelled or timed out, the report is older than `max_age`, the run published no report, or tests were only skipped. A failing
  test fails the run as well, and that run still counts, because the failure is the finding. Every finding ends with the repository,
  the commit SHA and the run ID, read from the run itself.

The code check behind it, `terraform.tests_pass`, also takes `test: <run block name>` to judge one test alone. A named test missing
from the report is unable to assess. That is how you give each control its own check, so a mapping can name it.

## 1. Write the controls as tests

In the IaC repository, add a test file with one `run` block per control, named after the control. A fictional example for Example
Pty Ltd's GRC platform is in [`examples/onboarding-tutorial/infra/oci`](../../examples/onboarding-tutorial/infra/oci/tests/controls.tftest.hcl):

```hcl
mock_provider "oci" {}

run "vm_has_no_public_ip" {
  command = plan
  assert {
    condition     = oci_core_instance.grc.create_vnic_details[0].assign_public_ip == "false"
    error_message = "vm_has_no_public_ip: the GRC platform's VM must not have a public IP address."
  }
}
```

Start each `error_message` with the control's name, so a failure reads well in CISO Assistant. Where the code itself can refuse a bad
value, add a variable validation and a run with `expect_failures` that shows the refusal, for example an office range of `0.0.0.0/0`.
Run them locally with `terraform test`; in this repository, `make test-terraform` runs the example's.

## 2. The pipeline publishes the report

```yaml
# .github/workflows/controls.yml in the IaC repository
name: Control tests
on:
  push: { branches: [main] }
  pull_request: {}
  schedule: [{ cron: "0 22 * * 0" }]
permissions:
  contents: read
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: hashicorp/setup-terraform@v3
      - run: terraform init -backend=false -input=false
      - run: terraform test -junit-xml=controls.xml
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: control-tests, path: controls.xml, retention-days: 14 }
```

The job needs no cloud credentials. `if: always()` publishes the report when a test fails, so the failure reaches CISO Assistant.

## 3. One check per control

Copy the profile into your GRC config repository under a name of your own, and add one check per control with `test:` set to its run
block. The tutorial's copy is
[`grc-infra-controls.yaml`](../../examples/onboarding-tutorial/profiles/grc-infra-controls.yaml):

```yaml
  - id: infrastructure.controls.vm_has_no_public_ip
    title: The GRC platform's VM has no public IP address
    over: [run, tests]
    code: terraform.tests_pass
    with: { max_age: "${max_age}", repository: "${repository}", test: vm_has_no_public_ip }
```

Copy its tests folder too, and add a case to its `expect:` lists for each new check, so `hound profile test` keeps passing.

## 4. A collector instance and a mapping

```yaml
collectors:
  - name: grc-infra-controls
    type: rest
    schedule: "30 23 * * *"
    settings:
      profile: grc-infra-controls
      repository: example/grc-infra
      workflow: controls.yml
    secrets:
      token: file:/run/secrets/iac_actions_token
```

| Parameter | Default | Meaning |
| --- | --- | --- |
| `repository` | required | The IaC repository, as `owner/name`. |
| `workflow` | required | The workflow file that runs the tests, such as `controls.yml`. |
| `branch` | `main` | The branch whose runs are read. |
| `artifact` | `control-tests` | The artefact holding `controls.xml`. |
| `max_age` | `P8D` | The oldest report still trusted, as an ISO 8601 duration. Make it longer than the schedule's interval. |
| `api_url` | `https://api.github.com/` | The GitHub API; GitHub Enterprise Server has its own. |

Then map each requirement to its control tests and to the drift check, as the tutorial's
[backup rows](../../examples/onboarding-tutorial/mappings/grc-platform-essential-eight-2023.yaml) do:

```yaml
  - ref: ISM-1814
    rule: all_pass
    checks:
      - infrastructure.controls.backups_retention_locked
      - infrastructure.drift.none
```

Controls with no row in your framework, such as "no SSH from the internet" for the Essential Eight, are still evidence: each run
keeps their findings with its evidence. They answer rows of later frameworks too, such as boundary protection in NIST SP 800-171.

## Limits

- GitHub Actions only, for now, as for the drift profiles. Any test tool that writes JUnit XML works the same way.
- The report is read as `controls.xml`, one file per artefact; the archive and the unzipped report both count towards the query's
  `max_bytes`.
- Why not an emulator such as LocalStack? It emulates AWS only, its free plan is for non-commercial use and needs an account, and on
  that plan VMs and VPNs are records only. Mock providers prove the same configuration for any cloud with nothing to run.
