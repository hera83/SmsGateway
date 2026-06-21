# syntax=docker/dockerfile:1
#
# Multi-stage build for the SmsGateway API (.NET 10).
# Build context is expected to be the repository root, e.g.:
#   docker build -f api/Dockerfile -t sms-gateway-api .

########## Stage 1: restore + publish ##########
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY api/api.csproj api/
RUN dotnet restore api/api.csproj

COPY api/ api/
RUN dotnet publish api/api.csproj -c Release -o /app/publish --no-restore

########## Stage 2: runtime ##########
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# System.IO.Ports needs libudev on Linux to enumerate/open serial (TTY/USB) devices.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libudev1 \
    && rm -rf /var/lib/apt/lists/*

# Non-root runtime user. UID/GID 1000 chosen so the host can align bind-mount
# ownership (e.g. `chown -R 1000:1000 ./App_dbs`) without guessing an ID.
# Membership in "dialout" matches the group that owns serial devices
# (/dev/ttyUSB*, /dev/ttyACM*, ...) on Debian/Ubuntu — see README.md for details
# on what to do if the device's group/permissions differ on the host.
RUN getent group dialout >/dev/null || groupadd --system dialout \
    && groupadd --gid 1000 smsgateway \
    && useradd --uid 1000 --gid smsgateway --groups dialout --create-home --shell /usr/sbin/nologin smsgateway \
    && mkdir -p /app/App_dbs \
    && chown -R smsgateway:smsgateway /app

COPY --from=build --chown=smsgateway:smsgateway /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    SERIAL_PORT=/dev/COM1
EXPOSE 8080

USER smsgateway

ENTRYPOINT ["dotnet", "api.dll"]
