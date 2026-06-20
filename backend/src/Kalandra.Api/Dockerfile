# Build on the native builder platform and cross-publish to the target arch.
# This avoids running dotnet restore/publish under QEMU emulation, which is
# extremely slow when the CI runner is amd64 and the target is arm64.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG COMMIT_SHA=unknown
ARG TARGETARCH
WORKDIR /src

# Copy only csproj files first so the restore layer is cached unless a
# csproj actually changes.
COPY src/Kalandra.Api/Kalandra.Api.csproj           src/Kalandra.Api/
COPY src/Kalandra.Infrastructure/Kalandra.Infrastructure.csproj src/Kalandra.Infrastructure/
COPY src/Kalandra.JobOffers/Kalandra.JobOffers.csproj src/Kalandra.JobOffers/

RUN dotnet restore src/Kalandra.Api/Kalandra.Api.csproj -a $TARGETARCH

# Copy the rest of the context (sources + root config such as .editorconfig, so
# analyzer severities apply in-container). .dockerignore defines what's excluded.
COPY . .
RUN dotnet publish src/Kalandra.Api/Kalandra.Api.csproj \
    -c Release \
    -a $TARGETARCH \
    --no-restore \
    -o /app \
    -p:SourceRevisionId=$COMMIT_SHA

# Runtime image — this one matches TARGETPLATFORM (arm64 in prod).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Ownership marker for the shared host: the deploy's image prune is scoped to
# this label, so it only ever removes this app's old images — never another
# app's or the shared Caddy image.
LABEL tech.kalandra.app=demopage

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Kalandra.Api.dll"]
