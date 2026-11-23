# Fixtures for the unattended local trial

`HOUND_TRIAL_YES=1 make trial` (and the `trial` job in CI) runs the trial with no agent and no person. Each stage then replays what an agent and a person would do, through the same `hound` commands, with these fictional files. Everything here is made up. The trial walks [the onboarding tutorial's deployment](../../docs/examples/onboarding-tutorial.md): CISO Assistant and Evidence Hound on a private Oracle Cloud VM (`GRC-PLATFORM`), reached over a site-to-site VPN through a Sophos XGS 88W (`OFFICE-FIREWALL`).

| File | Stands in for |
| --- | --- |
| `third-parties/sup-oracle.yaml` | Oracle Cloud Infrastructure, `GRC-PLATFORM`'s host, as the agent drafts it with `hound supplier draft` after a person approves it and concludes its review with `hound approve` |
| `third-parties/sup-sophos.yaml` | The same for Sophos, who makes `OFFICE-FIREWALL` and Sophos Central |
| `third-parties/sup-ciso-assistant.yaml` | The same for the open-source CISO Assistant project, which `GRC-PLATFORM` runs |
| `third-parties/sup-evidence-hound.yaml` | The same for the open-source Evidence Hound project, which `GRC-PLATFORM` runs |

Each system's decisions are the onboarding tutorial's, [`examples/onboarding-tutorial/answers/`](../onboarding-tutorial/answers) (`grc-platform.decisions.txt` and `office-firewall.decisions.txt`), typed into `hound onboard` as a person would. Each system's mapping is the tutorial's, from [`examples/onboarding-tutorial/mappings/`](../onboarding-tutorial/mappings). Guide: [docs/tutorials/local-trial.md](../../docs/tutorials/local-trial.md).
