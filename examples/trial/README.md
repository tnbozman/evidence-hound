# Fixtures for the unattended local trial

`HOUND_TRIAL_YES=1 make trial` (and the `trial` job in CI) runs the trial with no agent and no person. Each stage then replays what an agent and a person would do, through the same `hound` commands, with these fictional files. Everything here is made up.

| File | Stands in for |
| --- | --- |
| `third-parties/sup-sophos.yaml` | The Sophos supplier the agent drafts with `hound supplier draft`, after a person approves it and concludes its review with `hound approve` |
| `third-parties/sup-example-msp.yaml` | The same for the fictional managed service provider, Example Managed Services Pty Ltd |

The system's decisions are the onboarding tutorial's, [`examples/onboarding-tutorial/answers/office-firewall.decisions.txt`](../onboarding-tutorial/answers/office-firewall.decisions.txt), typed into `hound onboard` as a person would. Guide: [docs/tutorials/local-trial.md](../../docs/tutorials/local-trial.md).
