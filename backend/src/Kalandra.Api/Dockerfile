FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG COMMIT_SHA=unknown
WORKDIR /src

# Copy all csproj files and restore (preserves Docker layer caching)
COPY src/Kalandra.Api/Kalandra.Api.csproj src/Kalandra.Api/
COPY src/Kalandra.Infrastructure/Kalandra.Infrastructure.csproj src/Kalandra.Infrastructure/
COPY src/Kalandra.JobOffers/Kalandra.JobOffers.csproj src/Kalandra.JobOffers/
RUN dotnet restore src/Kalandra.Api/Kalandra.Api.csproj

# Copy everything and build
COPY src/ src/
WORKDIR /src/src/Kalandra.Api
RUN dotnet publish -c Release -o /app --no-restore -p:SourceRevisionId=$COMMIT_SHA

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Kalandra.Api.dll"]
