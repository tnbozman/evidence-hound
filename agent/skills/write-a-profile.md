---
name: write-a-profile
description: Write or change a REST profile that collects and assesses evidence from a system's API, with recorded tests. Use for the API checks accepted when a system was onboarded.
includes: [profile-authoring]
---
## Write a profile

A profile is a YAML file in `profiles/` that says what to collect from an API, what to extract and how to assess it. It holds no code and no credentials.

1. Start from the API checks decided for the system (`onboarding/<SYSTEM>/session.json`) and the operations each rests on.
2. Write `profiles/<name>.yaml`. Use only operations that read. Every check ends in pass, fail or unable to assess; never a guess.
3. Check it and run its recorded tests offline:
   `hound profile validate <name>` then `hound profile test <name>`
   Fix every problem by its code. Never edit a recorded response to make a test pass.
4. Sampling the real API needs credentials and the person's go-ahead: they run `hound onboard profile <system>` themselves. Do not run it for them.
