# syntax=docker/dockerfile:1-labs

# Build natively and cross-publish so restore/publish never run under slow QEMU emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:cc02b814237a76c4735fb6eeee87181597b5e56f2ce458a435e527674d91f4d7 AS build
ARG COMMIT_SHA=unknown
ARG TARGETARCH
WORKDIR /src

# --parents keeps the src/<Project>/ layout a glob COPY would flatten; restore must match publish's arch or publish rejects the assets file as RID-less.
COPY --parents src/*/*.csproj ./
RUN dotnet restore src/Kalandra.McpServer/Kalandra.McpServer.csproj -a $TARGETARCH

COPY . .
RUN dotnet publish src/Kalandra.McpServer/Kalandra.McpServer.csproj \
    -c Release \
    -a $TARGETARCH \
    --no-restore \
    -o /app \
    -p:SourceRevisionId=$COMMIT_SHA

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:254fe017495996d3abcc487ffacdcce2b73affb380d3af87af12c8dff41fe0b0 AS runtime
WORKDIR /app

# The deploy's image prune is scoped to this label so it never removes another app's images on the shared host.
LABEL tech.kalandra.app=demopage

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Kalandra.McpServer.dll"]
