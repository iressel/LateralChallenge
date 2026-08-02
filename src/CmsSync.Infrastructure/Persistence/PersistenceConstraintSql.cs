namespace CmsSync.Infrastructure.Persistence;

internal static class PersistenceConstraintSql
{
    public static string CreatePositiveCheck(string columnName)
    {
        return $"[{columnName}] > 0";
    }

    public static string CreateNonNegativeCheck(string columnName)
    {
        return $"[{columnName}] >= 0";
    }

    public static string CreateNullablePositiveCheck(string columnName)
    {
        return $"[{columnName}] IS NULL OR [{columnName}] > 0";
    }

    public static string CreateNullableNonNegativeCheck(string columnName)
    {
        return $"[{columnName}] IS NULL OR [{columnName}] >= 0";
    }

    public static string CreateJsonObjectCheck(string columnName)
    {
        return $"ISJSON([{columnName}], OBJECT) = 1";
    }
}
