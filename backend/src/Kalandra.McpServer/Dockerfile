# syntax=docker/dockerfile:1-labs

# Build on the native builder platform and cross-publish to the target arch.
# This avoids running dotnet restore/publish under QEMU emulation, which is
# extremely slow when the CI runner is amd64 and the target is arm64.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:cc02b814237a76c4735fb6eeee87181597b5e56f2ce458a435e527674d91f4d7 AS build
ARG COMMIT_SHA=unknown
ARG TARGETARCH
WORKDIR /src

# Restore from project files alone so this layer stays cached across
# source-only changes; --parents (labs syntax) keeps the src/<Project>/ layout
# that a plain glob COPY would flatten. Restore targets the same arch as
# publish, which otherwise rejects the assets file as RID-less.
COPY --parents src/*/*.csproj ./
RUN dotnet restore src/Kalandra.McpServer/Kalandra.McpServer.csproj -a $TARGETARCH

# Copy everything (.dockerignore is the blacklist) and publish the MCP host — its
# project references decide what's built, so adding a project needs no change here.
COPY . .
RUN dotnet publish src/Kalandra.McpServer/Kalandra.McpServer.csproj \
    -c Release \
    -a $TARGETARCH \
    --no-restore \
    -o /app \
    -p:SourceRevisionId=$COMMIT_SHA

# Runtime image — this one matches TARGETPLATFORM (arm64 in prod).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:254fe017495996d3abcc487ffacdcce2b73affb380d3af87af12c8dff41fe0b0 AS runtime
WORKDIR /app

# Ownership marker for the shared host: the deploy's image prune is scoped to
# this label, so it only ever removes this app's old images — never another
# app's or the shared Caddy image.
LABEL tech.kalandra.app=demopage

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Kalandra.McpServer.dll"]
