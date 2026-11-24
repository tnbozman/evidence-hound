# EvidenceHound.Templates

`dotnet new` templates for [Evidence Hound](https://github.com/tnbozman/evidence-hound), the evidence collector for CISO Assistant.

```console
$ dotnet new install EvidenceHound.Templates
$ dotnet new hound-collector -n Example.Widget
$ cd Example.Widget && dotnet test
```

`hound-collector` makes a collector project, a test project that runs the shared collector contract tests
(`EvidenceHound.Collectors.Testing`) against a recorded response, and a README with the steps to make it read your system. It references
the `EvidenceHound.Collectors.Abstractions` and `EvidenceHound.Collectors.Testing` packages of the same version as this template pack;
`--HoundVersion` picks another.

Guide: [writing a collector](https://github.com/tnbozman/evidence-hound/blob/main/docs/tutorials/write-a-collector.md).
