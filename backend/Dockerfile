# ChangeLens backend — multi-stage build (ASP.NET Core 10, non-root runtime).
#
# Build:  docker build -f backend/Dockerfile -t changelens-backend .
# Runtime: serves the API on port 5000; configuration via environment variables.
#
# Note on ordering: `COPY src/ ./src/` runs BEFORE `dotnet restore`. Restore
# regenerates every obj/ file (assets + nuget.g.props) container-natively, so a
# developer machine's Windows-generated obj/ (which carries host NuGet fallback
# paths) can never leak into the publish step even if it reaches the context.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ChangeLens.slnx ./
COPY Directory.Build.props ./
COPY src/ ./src/
RUN dotnet restore src/ChangeLens.Api/ChangeLens.Api.csproj
RUN dotnet publish src/ChangeLens.Api/ChangeLens.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is needed by the compose healthcheck (DB-gated /api/v1/health). git is
# needed by GitChangeSource for Roslyn/dependency analysis of the demo repository.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl git \
    && rm -rf /var/lib/apt/lists/*

# Non-root user (ASP.NET images ship an app user).
USER app

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "ChangeLens.Api.dll"]
COPY --from=build /app/publish ./
