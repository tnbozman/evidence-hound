# The IaC pipeline dev server

A fictional Example Pty Ltd IaC pipeline that answers the `terraform-tests` and `terraform-drift` profiles (and your copies of them, such as the onboarding tutorial's `grc-infra-controls`) on your machine. It stands in for the GitHub Actions API of the repository `example/grc-infra`, which holds the GRC platform's Oracle Cloud Terraform from [the onboarding tutorial](onboarding-tutorial.md). `make trial` uses it so the GRC platform's hosting controls are collected with no GitHub account and no token. Nothing leaves the machine.

```bash
make iac-dev                                                # every control in place, no drift, on port 5380
make iac-dev ARGS="--variant gaps"                          # every deviation below
make iac-dev ARGS="--deviation drift --port 5381"
make iac-dev ARGS="--controls-report /path/to/controls.xml" # serve the report your own terraform test wrote
```

The server prints the settings for each `rest` collector: `profile`, `api_url` (the server's address), `repository` and `workflow`. The secret `token` can be any text: the server checks none.

## What the pipeline holds

| Workflow | Run | Artefact |
| --- | --- | --- |
| `controls.yml` | One completed, successful run on `main`, finished a minute before the server started | `control-tests`, holding `controls.xml`: the JUnit report of the tutorial's [`controls.tftest.hcl`](../../examples/onboarding-tutorial/infra/oci/tests/controls.tftest.hcl), one test case per `run` block, every one passing |
| `drift.yml` | The same | `drift-plan`, holding `plan.json`: a refresh-only plan in which every resource of the tutorial's Terraform is a no-op and nothing drifted |

It answers these addresses, and any other gets 404:

| Address | Answer |
| --- | --- |
| `GET /repos/example/grc-infra/actions/workflows/{workflow}/runs` | The newest run, dated from the server's start, so `max_age` never makes it stale. |
| `GET /repos/example/grc-infra/actions/runs/{run}/artifacts` | The run's artefact. |
| `GET /repos/example/grc-infra/actions/artifacts/{artifact}/zip` | The artefact's zip, served directly rather than through GitHub's redirect. |

## A real report

With Terraform installed, run the tutorial's control tests and serve their real report:

```bash
cd examples/onboarding-tutorial/infra/oci && terraform init -backend=false && terraform test -junit-xml=/tmp/controls.xml
make iac-dev ARGS="--controls-report /tmp/controls.xml"
```

The tests use mock providers, so they need no Oracle account and create nothing. Change the Terraform (give the VM `assign_public_ip = true`, say), test again and collect again to watch a control fail. `make trial` does the first part for you when `terraform` is on the path.

## The variants

| Deviation | What changes | Check and expected outcome |
| --- | --- | --- |
| `public-ip` | The control test `vm_has_no_public_ip` fails: a change gave the VM a public address. | `infrastructure.controls.vm_has_no_public_ip` and `infrastructure.tests.pass`: fail |
| `drift` | The nightly plan finds the backup bucket changed outside Terraform. | `infrastructure.drift.none`: fail |

`IacDevServerTests` runs the rest collector with the library profiles and the tutorial's `grc-infra-controls` against the compliant pipeline and each deviation, so this table and the pipeline cannot drift apart. A test also checks the report has one test case per `run` block of the tutorial's control tests.

## Adding a control test or a deviation

- The pipeline is described once, in `src/EvidenceHound.IacDevServer/IacDevPipeline.cs`; `IacAnswers.cs` turns it into GitHub Actions JSON, the JUnit report and the plan.
- A new `run` block in `controls.tftest.hcl` adds a `ControlTest` (the test fails until it does).
- A deviation is a name, a sentence, the checks it fails with their expected outcome, and a change to the pipeline. Add it to `IacDevPipeline.Deviations` and a row to the table above.

## What it cannot show

- GitHub itself: rate limits, the artefact download redirect, expired artefacts and token scopes. The profiles' recorded tests and a run against your own IaC repository cover those.
- Your cloud: a mock-provider test proves what the code declares, and the drift plan stands in for a real refresh against Oracle Cloud.
