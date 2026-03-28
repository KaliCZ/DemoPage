FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy csproj and restore
COPY src/Kalandra.Api/Kalandra.Api.csproj src/Kalandra.Api/
RUN dotnet restore src/Kalandra.Api/Kalandra.Api.csproj

# Copy everything and build
COPY src/ src/
WORKDIR /src/src/Kalandra.Api
RUN dotnet publish -c Release -o /app --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Kalandra.Api.dll"]
