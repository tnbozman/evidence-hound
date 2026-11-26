# The GRC team's workspace for this repo: released hound and the .NET runtime it needs, with no SDK or build tools.
# hound comes from the runner image of the release the deployment runs (deploy/deploy.env), so both use the same version;
# scripts/stamp-release.sh in the Evidence Hound repo moves this pin with the others at each release.
ARG HOUND_IMAGE=ghcr.io/tnbozman/evidence-hound:1.0.1
FROM ${HOUND_IMAGE} AS hound
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime

FROM mcr.microsoft.com/devcontainers/base:ubuntu-24.04
COPY --from=runtime /usr/share/dotnet /usr/share/dotnet
COPY --from=hound /app /opt/evidence-hound
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true
# hound's default evidence root, owned by the workspace user so a trial run can write there.
RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
 && ln -s /opt/evidence-hound/hound /usr/local/bin/hound \
 && mkdir -p /var/lib/evidence-hound \
 && chown -R vscode:vscode /var/lib/evidence-hound
