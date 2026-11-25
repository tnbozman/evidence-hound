# syntax=docker/dockerfile:1
# Collector runner image: the `hound` CLI on a chiseled .NET runtime (no shell, no package manager).
# Build from the repo root: make image  (or docker build -f src/EvidenceHound.Cli/Dockerfile .)
# Runs as the non-root `app` user and works on a read-only root filesystem.
# Base images are pinned by digest; Dependabot keeps them current.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0@sha256:2794448a07118a6eaf00f29ab5d2bf44928517ce5ff86505a63a69230374f599 AS build
WORKDIR /src
# global.json is left out: it pins the dev SDK band, and this stage uses the SDK image pinned above.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
# hound init carries the Essential Eight mapping and snapshot, the compliance repo template's .gitignore and the weekly review runbook.
COPY mappings/essential-eight-2023.yaml mappings/
COPY frameworks/essential-eight-2023.json frameworks/
COPY examples/compliance-repo/.gitignore examples/compliance-repo/
COPY examples/compliance-repo/manual-sources/weekly-compliance-review.md examples/compliance-repo/manual-sources/
# hound onboard's default skills and the coding-assistant files hound agent setup writes, built into hound.
COPY skills/ skills/
COPY agent/ agent/
RUN dotnet restore src/EvidenceHound.Cli/EvidenceHound.Cli.csproj --locked-mode
# The SDK stage runs on the build machine's platform and publishes for the target architecture only
# (one of the csproj's RuntimeIdentifiers), so arm64 needs no emulation and other platforms' native
# libraries stay out of the image.
ARG TARGETARCH=amd64
# The image has no git history for MinVer, so the release workflow passes the tag's version (s13-04).
ARG HOUND_VERSION=1.0.0-dev
RUN rid="linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64)" \
 && dotnet publish src/EvidenceHound.Cli/EvidenceHound.Cli.csproj --no-restore -c Release -o /app \
      -r "$rid" --self-contained false -p:MinVerVersionOverride="$HOUND_VERSION" \
      -p:ContinuousIntegrationBuild=true -p:DebugType=none -p:GenerateDocumentationFile=false
# The runner's data folder (evidence, run records, manual drop folders), owned by the non-root `app` user (1654)
# so a named volume mounted there starts writable.
RUN mkdir -p /data/var/lib/evidence-hound

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:1b234cef150624fe8cc7d027e8d742c0f03a16aef2484dc469eb8c872167255e
LABEL org.opencontainers.image.title="Evidence Hound runner" \
      org.opencontainers.image.description="Collects compliance evidence and publishes it to CISO Assistant" \
      org.opencontainers.image.source="https://github.com/tnbozman/evidence-hound" \
      org.opencontainers.image.licenses="Apache-2.0"
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true
WORKDIR /app
COPY --from=build /app ./
# The chiseled runtime has no time zone database, and schedules run in scheduler.time_zone (an IANA name such as
# Australia/Sydney), so the build stage's copy (the same Ubuntu release) is added.
COPY --from=build /usr/share/zoneinfo /usr/share/zoneinfo
COPY frameworks/ /app/frameworks/
COPY mappings/ /app/mappings/
COPY --from=build --chown=1654:1654 /data/var/lib/evidence-hound /var/lib/evidence-hound
USER app
ENTRYPOINT ["/app/hound"]
