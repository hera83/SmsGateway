# syntax=docker/dockerfile:1
#
# Multi-stage build for the SmsGateway web dashboard (.NET 10).
# Build context is expected to be the repository root, e.g.:
#   docker build -f web/Dockerfile -t sms-gateway-web .

########## Stage 1: restore + publish ##########
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY web/web.csproj web/
RUN dotnet restore web/web.csproj

COPY web/ web/
RUN dotnet publish web/web.csproj -c Release -o /app/publish --no-restore

########## Stage 2: runtime ##########
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Non-root runtime user. UID/GID 1000 chosen so the host can align bind-mount
# ownership (e.g. `chown -R 1000:1000 ./web/App_dbs`) without guessing an ID.
RUN groupadd --gid 1000 smsgatewayweb \
    && useradd --uid 1000 --gid smsgatewayweb --create-home --shell /usr/sbin/nologin smsgatewayweb \
    && mkdir -p /app/App_dbs \
    && chown -R smsgatewayweb:smsgatewayweb /app

COPY --from=build --chown=smsgatewayweb:smsgatewayweb /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

USER smsgatewayweb

ENTRYPOINT ["dotnet", "web.dll"]
