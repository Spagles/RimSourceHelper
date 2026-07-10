using Dapper;
using Microsoft.Data.Sqlite;
using RimAnalyzer.Models;

namespace RimAnalyzer.Database.Tables;

// Types 表的数据访问
public class TypeRepository(SqliteConnection connection)
{
    // 批量插入类型记录（跳过 FullName 冲突），返回插入数量
    public int BulkInsert(IEnumerable<TypeEntity> types)
    {
        const string sql = """
            INSERT OR IGNORE INTO Types (Namespace, Name, FullName, BaseType, IsAbstract, IsInterface, IsEnum, IsSealed, Accessibility, AssemblyName, AssemblyPath, SourceId)
            VALUES (@Namespace, @Name, @FullName, @BaseType, @IsAbstract, @IsInterface, @IsEnum, @IsSealed, @Accessibility, @AssemblyName, @AssemblyPath, @SourceId)
            """;

        using var transaction = connection.BeginTransaction();
        var count = connection.Execute(sql, types, transaction);
        transaction.Commit();
        return count;
    }

    // 按完全限定名精确查找
    public TypeEntity? FindByFullName(string fullName)
    {
        const string sql = "SELECT * FROM Types WHERE FullName = @fullName";
        return connection.QueryFirstOrDefault<TypeEntity>(sql, new { fullName });
    }

    // 按名称模糊搜索
    public IEnumerable<TypeEntity> SearchByName(string pattern)
    {
        const string sql = "SELECT * FROM Types WHERE Name LIKE @pattern OR FullName LIKE @pattern";
        return connection.Query<TypeEntity>(sql, new { pattern = $"%{pattern}%" });
    }

    // 获取指定类型 ID
    public long? GetIdByFullName(string fullName)
    {
        const string sql = "SELECT Id FROM Types WHERE FullName = @fullName";
        return connection.QueryFirstOrDefault<long?>(sql, new { fullName });
    }

    // 获取所有 FullName → Id 映射（用于批量关联外键）
    public Dictionary<string, long> GetFullNameToIdMap()
    {
        const string sql = "SELECT FullName, Id FROM Types";
        return connection.Query<(string FullName, long Id)>(sql)
            .ToDictionary(x => x.FullName, x => x.Id);
    }
}
