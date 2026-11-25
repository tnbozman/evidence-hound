# Write a collector

Most systems need no code. If the system has a REST API, write a YAML profile for the built-in `rest` collector instead
([write a profile](write-a-profile.md)): profiles load from your compliance repo, are tested offline with `hound profile test`, and need
no new release of hound. Write a code collector only when a system needs something a profile cannot do, such as a protocol other than
HTTP or a vendor's signing scheme.

A code collector is a small .NET library: it implements `ICollector` from `EvidenceHound.Collectors.Abstractions`, calls one system,
and returns framework-neutral check results with evidence files. It never writes to the system it reads, and never talks to CISO
Assistant: hound maps and publishes its results.

## 1. Make the project

Download the `.nupkg` files from the [release](https://github.com/tnbozman/evidence-hound/releases) into a folder, such as
`~/hound-packages`, unless the packages are on nuget.org. Then, with the release's version:

```console
$ dotnet new install EvidenceHound.Templates@1.0.0 --add-source ~/hound-packages
$ dotnet new hound-collector -n Example.Widget
$ cd Example.Widget
$ dotnet restore --source ~/hound-packages --source https://api.nuget.org/v3/index.json
$ dotnet test
```

`dotnet restore` needs both sources: the Evidence Hound packages from the folder, everything else from nuget.org. Once the packages are
on nuget.org, `dotnet new install EvidenceHound.Templates@1.0.0` and `dotnet test` are enough.

The template references the `EvidenceHound.Collectors.Abstractions` and `EvidenceHound.Collectors.Testing` packages of its own
version. The generated README lists every file and the steps below in more detail.

## 2. Choose the checks

Name each check by what it proves, in the system's own terms, such as `widget.mfa.admins` or `widget.backup.daily`. Never use a
framework's control number: the mapping file of each framework ([mapping files](../mapping-files.md)) says which checks answer which
requirement, so one collector serves the Essential Eight, NIST SP 800-171 and whatever comes next.

## 3. Test first, from recorded responses

1. Save a **synthetic** response for each API call in `recorded/`: the shape of the real one, with made-up values (`example.com`,
   "Example Pty Ltd", no tokens). Never commit a real response.
2. Write a failing test for each check in `WidgetCollectorTests`, then make it pass. Keep the evaluation pure: response in,
   `CheckResult` out.
3. The contract tests (`WidgetCollectorContractTests`) run the shared kit from `EvidenceHound.Collectors.Testing` against your
   collector: it describes at least one check and emits only the checks it declares, its evidence files can be hashed, it honours
   cancellation, it returns a failed result rather than throwing when credentials are refused, and it never logs a secret. Read-only
   access is up to you: ask the system for read-only permissions and call only read endpoints. They must pass unchanged. The kit is documented in
   [its README](../../src/EvidenceHound.Collectors.Testing/README.md).

## 4. Bring it into hound

The runner loads no code at run time, so a code collector ships inside hound. When it is ready:

1. Open a pull request that adds the project under `src/Collectors/` and its tests under `tests/`, following
   [CONTRIBUTING.md](../../CONTRIBUTING.md).
2. Register it in `src/EvidenceHound.Runtime/Collectors/BuiltInCollectorServiceCollectionExtensions.cs` with
   `AddCollector<WidgetCollector>()`, or the extension method the template made.
3. Add its checks to a mapping file, and a page under `docs/collectors/` with the read-only permissions it needs.

`make test-template` (also a CI job) generates a collector from the template against freshly packed packages, and checks it builds and
its tests pass unmodified.
