# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until the project file changes.
COPY global.json Directory.Build.props ./
COPY src/AdoHookReceiver/AdoHookReceiver.csproj src/AdoHookReceiver/
RUN dotnet restore src/AdoHookReceiver/AdoHookReceiver.csproj

COPY src/ src/
RUN dotnet publish src/AdoHookReceiver/AdoHookReceiver.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Non-root user and port 8080 (ASPNETCORE_HTTP_PORTS) come from the aspnet image.
USER $APP_UID
EXPOSE 8080

# The aspnet image has no curl, so the app checks its own /health.
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["dotnet", "AdoHookReceiver.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "AdoHookReceiver.dll"]
