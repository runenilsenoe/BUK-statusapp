# BUK Status website. Built by Coolify from docker-compose.yaml; also works with plain `docker build .`.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Restore first so the package layer is cached until a project file changes.
COPY src/BukStatus.Core/BukStatus.Core.csproj src/BukStatus.Core/
COPY src/BukStatus.Web/BukStatus.Web.csproj src/BukStatus.Web/
RUN dotnet restore src/BukStatus.Web/BukStatus.Web.csproj

COPY src/ src/
RUN dotnet publish src/BukStatus.Web/BukStatus.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# curl is only for the container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# /app/data holds the SQLite database and the login cookie keys; mount a volume here.
RUN mkdir -p /app/data/keys && chown -R $APP_UID /app/data
VOLUME /app/data

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__BukStatus="Data Source=/app/data/bukstatus.db" \
    DataProtection__KeysPath=/app/data/keys

EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "BukStatus.Web.dll"]
