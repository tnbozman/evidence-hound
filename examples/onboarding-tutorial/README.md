# Onboarding tutorial: the files it writes

What [the onboarding tutorial](../../docs/tutorials/onboarding-tutorial.md) adds to a copy of the template compliance repo
(`examples/compliance-repo`), at the same paths, so you can compare your result with it. Everything is fictional.

- `answers/`: what the reviewer types at each prompt of `hound onboard`, one file per system and run, to replay the tutorial.
- `scope/third-parties/`: the four suppliers. Oracle and Sophos were drafted by `hound onboard` and reviewed; the open-source projects
  were written by hand.
- `scope/setup.yaml`: the template's, with the two systems onboarding added at the end.
- `scope/audits/`, `onboarding/`: each system's Essential Eight audit and onboarding record, as `hound onboard` wrote them, with a schema
  comment line added.
- `hound.yaml`, `mappings/`, `deploy/compose.override.yml`: the template's, with the three collectors (Sophos Central, drift and the
  control tests), the two systems' targets and their secrets.
- `profiles/grc-infra-controls.yaml` and its tests: the tutorial's copy of the `terraform-tests` profile, one check per hosting control.
- `infra/oci/`: the GRC platform's OCI Terraform, its control tests (`terraform test` with mock providers, no Oracle account needed) and
  the pipeline that publishes their report. In a real setup this lives in its own IaC repository.

The template itself stays without them, so a new compliance repo starts empty and applies in one go: these systems are registered only
once their suppliers are approved in CISO Assistant.
