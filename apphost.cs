#:sdk Aspire.AppHost.Sdk@13.4.0
#:property ManagePackageVersionsCentrally=false
#:package Aspire.Hosting.SqlServer@13.4.0
// Security pin: keeps NuGet Audit enabled by overriding vulnerable transitive MessagePack 2.5.192 in AppHost restore graph.
#:package MessagePack@3.1.8

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89";
const string SqlServerImageRepository = "mssql/server";
const string SqlServerContainerImageRepository = "mcr.microsoft.com/mssql/server";
const string SqlServerImageTag = "2022-CU26-ubuntu-22.04";
const string SqlServerImageSha256 = "ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89";
const string DefaultSqlDataVolumeName = "cms-sync-aspire-sql-data";
const string ApiAspNetCoreUrls = "http://+:8080";
const int SqlServerHostPort = 14333;
const int ApiHttpPort = 8080;

var builder = DistributedApplication.CreateBuilder(args);

var sqlDataVolumeName = builder.Configuration["Aspire:SqlDataVolumeName"];
if (string.IsNullOrWhiteSpace(sqlDataVolumeName))
{
    sqlDataVolumeName = DefaultSqlDataVolumeName;
}

var mssqlSaPassword = builder.AddParameter("mssql-sa-password", secret: true);
var migrationSqlPassword = builder.AddParameter("migration-sql-password", secret: true);
var writeSqlPassword = builder.AddParameter("write-sql-password", secret: true);
var readSqlPassword = builder.AddParameter("read-sql-password", secret: true);

var cmsUsername = builder.AddParameter("cms-username", secret: true);
var cmsPassword = builder.AddParameter("cms-password", secret: true);
var consumerUsername = builder.AddParameter("consumer-username", secret: true);
var consumerPassword = builder.AddParameter("consumer-password", secret: true);
var administratorUsername = builder.AddParameter("administrator-username", secret: true);
var administratorPassword = builder.AddParameter("administrator-password", secret: true);

var sql = builder.AddSqlServer("sql", mssqlSaPassword, port: SqlServerHostPort)
    .WithImage(SqlServerImageRepository, SqlServerImageTag)
    .WithImageSHA256(SqlServerImageSha256)
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithDataVolume(sqlDataVolumeName)
    .WithLifetime(ContainerLifetime.Persistent);

var dbInit = builder.AddContainer("db-init", SqlServerContainerImageRepository, SqlServerImageTag)
    .WithImageSHA256(SqlServerImageSha256)
    .WithBindMount("./scripts/container", "/opt/cms-sync", isReadOnly: true)
    .WithEntrypoint("/bin/bash")
    .WithArgs("/opt/cms-sync/initialize-database.sh")
    .WithEnvironment("MSSQL_SA_PASSWORD", mssqlSaPassword)
    .WithEnvironment("MIGRATION_SQL_PASSWORD", migrationSqlPassword)
    .WithEnvironment("WRITE_SQL_PASSWORD", writeSqlPassword)
    .WithEnvironment("READ_SQL_PASSWORD", readSqlPassword)
    .WaitFor(sql);

var migration = builder.AddDockerfile("migration", ".", "Dockerfile", "migration")
    .WithBuildArg("SQL_SERVER_IMAGE", SqlServerImage)
    .WithEnvironment("MIGRATION_SQL_PASSWORD", migrationSqlPassword)
    .WaitForCompletion(dbInit);

var sqlEndpoint = sql.Resource.PrimaryEndpoint;

var writeConnectionString = ReferenceExpression.Create(
    $"Server={sqlEndpoint.Property(EndpointProperty.IPV4Host)},{sqlEndpoint.Property(EndpointProperty.Port)};Database=CmsSync;User ID=CmsSyncWriter;Password={writeSqlPassword};Encrypt=True;TrustServerCertificate=True;Persist Security Info=False");

var readConnectionString = ReferenceExpression.Create(
    $"Server={sqlEndpoint.Property(EndpointProperty.IPV4Host)},{sqlEndpoint.Property(EndpointProperty.Port)};Database=CmsSync;User ID=CmsSyncReader;Password={readSqlPassword};Encrypt=True;TrustServerCertificate=True;Persist Security Info=False;ApplicationIntent=ReadOnly");

builder.AddProject("api", "src/CmsSync.Api/CmsSync.Api.csproj")
    .WithHttpEndpoint(targetPort: ApiHttpPort, port: ApiHttpPort, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", ApiAspNetCoreUrls)
    .WithEnvironment("RestoreSources", "https://api.nuget.org/v3/index.json")
    .WithEnvironment("ConnectionStrings__WriteDatabase", writeConnectionString)
    .WithEnvironment("ConnectionStrings__ReadDatabase", readConnectionString)
    .WithEnvironment("Authentication__Credentials__Cms__Username", cmsUsername)
    .WithEnvironment("Authentication__Credentials__Cms__Password", cmsPassword)
    .WithEnvironment("Authentication__Credentials__Consumer__Username", consumerUsername)
    .WithEnvironment("Authentication__Credentials__Consumer__Password", consumerPassword)
    .WithEnvironment("Authentication__Credentials__Administrator__Username", administratorUsername)
    .WithEnvironment("Authentication__Credentials__Administrator__Password", administratorPassword)
    .WaitForCompletion(migration)
    .WithHttpHealthCheck("/health/ready", endpointName: "http")
    .WithUrlForEndpoint("http", url => url.DisplayText = "API root")
    .WithUrlForEndpoint("http", _ => new ResourceUrlAnnotation { Url = "/swagger", DisplayText = "Swagger UI" })
    .WithUrlForEndpoint("http", _ => new ResourceUrlAnnotation { Url = "/swagger/v1/swagger.json", DisplayText = "OpenAPI JSON" })
    .WithUrlForEndpoint("http", _ => new ResourceUrlAnnotation { Url = "/health/live", DisplayText = "Liveness" })
    .WithUrlForEndpoint("http", _ => new ResourceUrlAnnotation { Url = "/health/ready", DisplayText = "Readiness" });

builder.Build().Run();
