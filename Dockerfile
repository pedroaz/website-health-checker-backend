FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/HealthChecker/HealthChecker.csproj src/HealthChecker/packages.lock.json ./src/HealthChecker/
RUN dotnet restore src/HealthChecker --locked-mode
COPY src/ ./src/
RUN dotnet publish src/HealthChecker -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HealthChecker.dll"]
FROM build AS tests
COPY tests/ ./tests/
RUN dotnet restore tests/HealthChecker.Tests --locked-mode
ENTRYPOINT ["dotnet", "test", "tests/HealthChecker.Tests", "--no-restore", "--logger", "trx;LogFileName=backend.trx", "--results-directory", "/results"]
