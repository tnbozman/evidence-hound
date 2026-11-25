# EvidenceHound.Cli

`hound`, the command line of [Evidence Hound](https://github.com/tnbozman/evidence-hound): it collects compliance evidence from your
systems on a schedule, maps it to framework requirements (Essential Eight, NIST SP 800-171 crosswalk, AI6) and keeps CISO Assistant,
your system of record, current. It also onboards systems and suppliers with your own coding agent, prepares one weekly review, and
builds committee and board reports.

```console
$ dotnet tool install --global EvidenceHound.Cli
$ hound init my-compliance --org "Example Pty Ltd"
$ hound --help
```

The runner image `ghcr.io/tnbozman/evidence-hound` carries the same `hound` for scheduled runs next to CISO Assistant. Start with the
[getting started guide](https://github.com/tnbozman/evidence-hound#readme).
