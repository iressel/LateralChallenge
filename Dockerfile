# syntax=docker/dockerfile:1

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.10@sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b
ARG SQL_SERVER_IMAGE=mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props LateralChallenge.sln ./
COPY .config/dotnet-tools.json .config/dotnet-tools.json
COPY src/CmsSync.Domain/CmsSync.Domain.csproj src/CmsSync.Domain/
COPY src/CmsSync.Application/CmsSync.Application.csproj src/CmsSync.Application/
COPY src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj src/CmsSync.Infrastructure/
COPY src/CmsSync.Api/CmsSync.Api.csproj src/CmsSync.Api/

RUN dotnet restore src/CmsSync.Api/CmsSync.Api.csproj \
    --source https://api.nuget.org/v3/index.json
RUN dotnet tool restore \
    --add-source https://api.nuget.org/v3/index.json

COPY src/ src/
RUN dotnet publish src/CmsSync.Api/CmsSync.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false && \
    dotnet ef migrations script \
    --idempotent \
    --project src/CmsSync.Infrastructure/CmsSync.Infrastructure.csproj \
    --startup-project src/CmsSync.Api/CmsSync.Api.csproj \
    --context CmsWriteDbContext \
    --configuration Release \
    --no-build \
    --output /app/migrations.sql

FROM ${SQL_SERVER_IMAGE} AS migration
COPY --chmod=0555 scripts/container/apply-migrations.sh /usr/local/bin/apply-cms-migrations
COPY --from=build /app/migrations.sql /opt/cms-sync/migrations.sql
ENTRYPOINT ["/usr/local/bin/apply-cms-migrations"]

FROM ${DOTNET_ASPNET_IMAGE} AS api
WORKDIR /app
COPY --from=build --chown=app:app /app/publish/ ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "CmsSync.Api.dll"]
