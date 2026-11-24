# syntax=docker/dockerfile:1-labs

# Build natively and cross-publish so restore/publish never run under slow QEMU emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0-alpine@sha256:52556c0fb9d0f609ae9aeab9efeeba96f4acc2f7038f7696f2235ccbdbb8e290 AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:11.0-alpine@sha256:d39b72ad1534fa0faf52b488bffb0762df7ba269d120f0338713d4dda2a41bf5 AS runtime
WORKDIR /app

# The deploy's image prune is scoped to this label so it never removes another app's images on the shared host.
LABEL tech.kalandra.app=demopage

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Kalandra.McpServer.dll"]
