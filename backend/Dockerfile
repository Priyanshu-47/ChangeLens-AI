# ChangeLens backend — multi-stage build (ASP.NET Core 10, non-root runtime).
#
# Build:  docker build -f backend/Dockerfile -t changelens-backend .
# Runtime: serves the API on port 5000; configuration via environment variables.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for layer caching.
COPY ChangeLens.slnx ./
COPY Directory.Build.props ./
COPY src/ChangeLens.Api/ChangeLens.Api.csproj src/ChangeLens.Api/
COPY src/ChangeLens.Application/ChangeLens.Application.csproj src/ChangeLens.Application/
COPY src/ChangeLens.Domain/ChangeLens.Domain.csproj src/ChangeLens.Domain/
COPY src/ChangeLens.Infrastructure/ChangeLens.Infrastructure.csproj src/ChangeLens.Infrastructure/
RUN dotnet restore src/ChangeLens.Api/ChangeLens.Api.csproj

COPY src/ ./src/
RUN dotnet publish src/ChangeLens.Api/ChangeLens.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root user (ASP.NET images ship an app user).
USER app

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "ChangeLens.Api.dll"]
COPY --from=build /app/publish ./
