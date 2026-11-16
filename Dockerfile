# syntax=docker/dockerfile:1
# Container images of the .NET hosts: the API, the worker and the CLI.
# Build one image with: docker build --target api --tag reliquary/api:local .
# The web app has its own Dockerfile in web/.

# The build stage runs on the platform of the builder. The output is portable .NET code, so each target platform uses it.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /source

# Restore first, so a change of the code does not restore the packages again.
COPY global.json Directory.Build.props Directory.Packages.props Reliquary.slnx ./
COPY src/Reliquary.BuildingBlocks/*.csproj src/Reliquary.BuildingBlocks/
COPY src/Reliquary.Platform/*.csproj src/Reliquary.Platform/
COPY src/Reliquary.Hosting/*.csproj src/Reliquary.Hosting/
COPY src/Reliquary.Api/*.csproj src/Reliquary.Api/
COPY src/Reliquary.Worker/*.csproj src/Reliquary.Worker/
COPY src/Reliquary.Cli/*.csproj src/Reliquary.Cli/
COPY src/Components/Reliquary.Accounts/*.csproj src/Components/Reliquary.Accounts/
COPY src/Components/Reliquary.Cards/*.csproj src/Components/Reliquary.Cards/
COPY src/Components/Reliquary.Collection/*.csproj src/Components/Reliquary.Collection/
COPY src/Components/Reliquary.Decks/*.csproj src/Components/Reliquary.Decks/
COPY src/Components/Reliquary.Playtest/*.csproj src/Components/Reliquary.Playtest/
RUN dotnet restore src/Reliquary.Api && dotnet restore src/Reliquary.Worker && dotnet restore src/Reliquary.Cli

COPY src/ src/
# The CI gives the version and the commit of the image. A local build uses the version of Directory.Build.props.
ARG RELIQUARY_VERSION=
ARG RELIQUARY_COMMIT=
RUN set -e; \
    properties=""; \
    if [ -n "$RELIQUARY_VERSION" ]; then properties="$properties -p:Version=$RELIQUARY_VERSION"; fi; \
    if [ -n "$RELIQUARY_COMMIT" ]; then properties="$properties -p:SourceRevisionId=$RELIQUARY_COMMIT"; fi; \
    dotnet publish src/Reliquary.Api --no-restore --configuration Release --output /app/api $properties; \
    dotnet publish src/Reliquary.Worker --no-restore --configuration Release --output /app/worker $properties; \
    dotnet publish src/Reliquary.Cli --no-restore --configuration Release --output /app/cli $properties

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER $APP_UID

FROM runtime AS api
COPY --from=build /app/api .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Reliquary.Api.dll"]

FROM runtime AS worker
COPY --from=build /app/worker .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Reliquary.Worker.dll"]

FROM runtime AS cli
# The backup commands run pg_dump and pg_restore. Their major version must be the version of the PostgreSQL server.
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends postgresql-common \
    && /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y \
    && apt-get install --yes --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
COPY --from=build /app/cli .
# The files of a deployed instance. The script reliquary-update reads them from the image of each version.
COPY deploy/compose.yaml deploy/.env.example deploy/reliquary-update deploy/reliquary-update.ps1 /app/deploy/
ENTRYPOINT ["dotnet", "reliquary.dll"]
