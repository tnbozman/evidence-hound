# Onboarding tutorial: the files it writes

What [the onboarding tutorial](../../docs/examples/onboarding-tutorial.md) adds to a copy of the template compliance repo
(`examples/compliance-repo`), at the same paths, so you can compare your result with it. Everything is fictional.

- `answers/`: what the reviewer types at each prompt of `hound onboard`, one file per system and run, to replay the tutorial.
- `scope/third-parties/`: the four suppliers. Oracle and Sophos were drafted by `hound onboard` and reviewed; the open-source projects
  were written by hand.
- `scope/setup.yaml`: the template's, with the two systems onboarding added at the end.
- `scope/audits/`, `onboarding/`: each system's Essential Eight audit and onboarding record, as `hound onboard` wrote them, with a schema
  comment line added.
- `hound.yaml`, `mappings/office-firewall-essential-eight-2023.yaml`, `deploy/compose.override.yml`: the template's, with the two
  collectors, the firewall's target and their secrets.

The template itself stays without them, so a new compliance repo starts empty and applies in one go: these systems are registered only
once their suppliers are approved in CISO Assistant.
