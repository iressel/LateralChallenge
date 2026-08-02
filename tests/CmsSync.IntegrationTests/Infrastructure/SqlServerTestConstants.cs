namespace CmsSync.IntegrationTests.Infrastructure;

public static class SqlServerTestConstants
{
    public const string Image =
        "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:" +
        "ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89";

    public const string MigrationId = "20260802142305_InitialCmsPersistence";
}
