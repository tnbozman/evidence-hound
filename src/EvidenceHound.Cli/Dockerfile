# syntax=docker/dockerfile:1
# Collector runner image: the `hound` CLI on a chiseled .NET runtime (no shell, no package manager).
# Build from the repo root: make image  (or docker build -f src/EvidenceHound.Cli/Dockerfile .)
# Runs as the non-root `app` user and works on a read-only root filesystem.
# Base images are pinned by digest; Dependabot keeps them current.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
WORKDIR /src
# global.json is left out: it pins the dev SDK band, and this stage uses the SDK image pinned above.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore src/EvidenceHound.Cli/EvidenceHound.Cli.csproj --locked-mode
# The SDK stage runs on the build machine's platform and publishes for the target architecture only
# (one of the csproj's RuntimeIdentifiers), so arm64 needs no emulation and other platforms' native
# libraries stay out of the image.
ARG TARGETARCH=amd64
RUN rid="linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64)" \
 && dotnet publish src/EvidenceHound.Cli/EvidenceHound.Cli.csproj --no-restore -c Release -o /app \
      -r "$rid" --self-contained false \
      -p:ContinuousIntegrationBuild=true -p:DebugType=none -p:GenerateDocumentationFile=false

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
COPY frameworks/ /app/frameworks/
COPY mappings/ /app/mappings/
USER app
ENTRYPOINT ["/app/hound"]
