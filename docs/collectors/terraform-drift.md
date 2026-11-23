# Infrastructure drift from your IaC pipeline

The `terraform-drift` profiles tell you whether the cloud infrastructure still matches its reviewed infrastructure code, as evidence
like any other check. They are ordinary [REST profiles](rest.md): `hound` never runs Terraform, never reads Terraform state and never
holds cloud credentials. Your IaC pipeline already has those. It makes the plan, publishes it as a build artefact, and `hound` reads the
artefact through the GitHub Actions API.

| Profile | What it does | Token |
| --- | --- | --- |
| `terraform-drift` | Reads the newest completed run of the drift workflow, its artefact and the plan in it. Read only. The default. | Fine-grained, on the IaC repository only, **Actions: read**. |
| `terraform-drift-trigger` | Starts the drift workflow (`workflow_dispatch`), polls the run until it completes, then reads its artefact. The one write any library profile makes, so it is opt-in twice: the profile marks the request `write: true`, and the collector instance must set `allow_writes: true`. | Fine-grained, on the IaC repository only, **Actions: read and write**. Nothing else. |

Both have one check:

- `infrastructure.drift.none`: **pass** when the plan lists no drift (`resource_drift`) and no change (`resource_changes` other than
  `no-op` or `read`); **fail** naming each changed resource address and its actions, for example
  `oci_core_instance.grc (update)`; **unable to assess** when there is no completed run, the newest run did not succeed, its plan is
  older than `max_age`, or it published no plan artefact, saying which. Every finding ends with where the plan came from: the
  repository, the commit SHA and the run ID, read from the run itself, not from the artefact's name.

## 1. The pipeline makes the plan

In the IaC repository, add a workflow that makes a refresh-only plan on a schedule and publishes it as `plan.json` in an artefact
named `drift-plan`. A fictional example for Example Pty Ltd:

```yaml
# .github/workflows/drift.yml in the IaC repository
name: Drift
on:
  schedule: [{ cron: "0 22 * * *" }]
  workflow_dispatch: {}            # only needed for terraform-drift-trigger
permissions:
  contents: read
jobs:
  plan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: hashicorp/setup-terraform@v3
      - run: terraform init -input=false
      - run: terraform plan -refresh-only -input=false -out=drift.tfplan
      - run: terraform show -json drift.tfplan > plan.json
      - uses: actions/upload-artifact@v4
        with: { name: drift-plan, path: plan.json, retention-days: 14 }
```

The cloud credentials stay in that pipeline, as they already do. Drift is the finding, not a pipeline failure: if you use
`-detailed-exitcode`, treat exit code 2 (changes) as success so the run still publishes the plan, because a run that does not succeed
is unable to assess. A normal `terraform plan` works too; its `resource_changes` are checked as well.

## 2. A collector instance

In `hound.yaml` of your GRC config repository:

```yaml
collectors:
  - name: grc-infra-drift
    type: rest
    schedule: "0 23 * * *"                      # after the pipeline's nightly plan
    settings:
      profile: terraform-drift
      repository: example/grc-infra
      workflow: drift.yml
    secrets:
      token: file:/run/secrets/iac_actions_token
```

| Parameter | Default | Meaning |
| --- | --- | --- |
| `repository` | required | The IaC repository, as `owner/name`. |
| `workflow` | required | The workflow file that makes the plan, such as `drift.yml`. |
| `branch` | `main` | The branch whose runs are read (or that the trigger runs on). |
| `artifact` | `drift-plan` | The artefact holding `plan.json`. |
| `max_age` | `P2D` | The oldest plan still trusted, as an ISO 8601 duration. Make it longer than the schedule's interval. |
| `api_url` | `https://api.github.com/` | The GitHub API; GitHub Enterprise Server has its own. |

| Secret | Meaning |
| --- | --- |
| `token` | The fine-grained token above, as a secret reference. |

GitHub answers an artefact download with a redirect to signed storage. The profiles follow it only to the hosts in the query's
`redirect_hosts` (`*.actions.githubusercontent.com` and `*.blob.core.windows.net`), with a plain GET that carries no token. If GitHub
moves its artefact storage, the download is refused (`rest.redirect_refused`) naming the new host: add it to your copy of the profile.

To start the workflow first, use `profile: terraform-drift-trigger` and add `allow_writes: true` to the instance's `settings`. Without it the
trigger is refused, nothing is sent, and the check is unable to assess. The trigger waits up to 30 minutes for the run.

## Where the Terraform lives

**Recommended: separate repositories.** The IaC repository holds the Terraform and its pipeline; the GRC config repository holds
`hound.yaml`, the scope and the profiles. Each has its own reviewers and its own token, and the drift token reads only the IaC
repository's Actions. Every finding names the IaC repository and commit, so it is clear which version was assessed.

**A monorepo works too.** Point `repository` at the one repository; the workflow and artefact are named the same way.

## Limits

- GitHub Actions only, for now. GitLab and Azure DevOps need only new profiles in the same shape, with no code.
- One plan per artefact, read as `plan.json`; the archive and the unzipped plan both count towards the query's `max_bytes`.
