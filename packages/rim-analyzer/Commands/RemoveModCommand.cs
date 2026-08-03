using System.CommandLine;
using System.Text.Json;
using Dapper;
using RimAnalyzer.Database;

namespace RimAnalyzer.Commands;

// remove-mod 子命令：从数据库中移除指定 Mod 的所有数据
public static class RemoveModCommand
{
    public static Command Create()
    {
        var nameOption = new Option<string>("--name")
        {
            Description = "Name of the mod to remove",
            Required = true
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Database path",
            Required = true
        };

        var command = new Command("remove-mod", "Remove a mod's data from the database")
        {
            nameOption,
            dbOption
        };

        command.SetAction((parseResult, _) =>
        {
            var name = parseResult.GetValue(nameOption)!;
            var dbPath = parseResult.GetValue(dbOption)!;

            try
            {
                Execute(name, dbPath);
                Console.WriteLine(JsonSerializer.Serialize(new { status = "success", removed = name }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "error", error = ex.Message }));
                Environment.ExitCode = 1;
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static void Execute(string name, string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"Database not found: {dbPath}");

        using var db = DatabaseContext.Open(dbPath, force: false);
        var source = db.Sources.FindByName(name);

        // 幂等：源不存在时直接返回成功
        if (source is null)
        {
            Log($"[INFO] Source '{name}' not found, nothing to remove.");
            return;
        }

        var conn = db.Connection;
        var sourceId = source.Id;

        // 事务保护：原子删除所有关联数据
        using var transaction = conn.BeginTransaction();
        conn.Execute("DELETE FROM XmlPatches WHERE SourceId = @sourceId", new { sourceId }, transaction);
        conn.Execute("DELETE FROM HarmonyPatches WHERE SourceId = @sourceId", new { sourceId }, transaction);
        conn.Execute("DELETE FROM Calls WHERE CallerMethodId IN (SELECT Id FROM Methods WHERE SourceId = @sourceId) OR CalleeMethodId IN (SELECT Id FROM Methods WHERE SourceId = @sourceId)", new { sourceId }, transaction);
        conn.Execute("DELETE FROM FieldAccesses WHERE MethodId IN (SELECT Id FROM Methods WHERE SourceId = @sourceId) OR FieldId IN (SELECT Id FROM Fields WHERE SourceId = @sourceId)", new { sourceId }, transaction);
        conn.Execute("DELETE FROM Inheritance WHERE ParentTypeId IN (SELECT Id FROM Types WHERE SourceId = @sourceId) OR ChildTypeId IN (SELECT Id FROM Types WHERE SourceId = @sourceId)", new { sourceId }, transaction);
        conn.Execute("DELETE FROM DefReferences WHERE SourceDefId IN (SELECT Id FROM Defs WHERE SourceId = @sourceId)", new { sourceId }, transaction);
        conn.Execute("DELETE FROM Properties WHERE SourceId = @sourceId", new { sourceId }, transaction);
        conn.Execute("DELETE FROM Fields WHERE SourceId = @sourceId", new { sourceId }, transaction);
        conn.Execute("DELETE FROM Methods WHERE SourceId = @sourceId", new { sourceId }, transaction);
        conn.Execute("DELETE FROM Types WHERE SourceId = @sourceId", new { sourceId }, transaction);
        conn.Execute("DELETE FROM Defs WHERE SourceId = @sourceId", new { sourceId }, transaction);
        conn.Execute("DELETE FROM Sources WHERE Id = @sourceId", new { sourceId }, transaction);
        transaction.Commit();

        Log($"[INFO] Removed source: {name} (id={sourceId})");
    }

    private static void Log(string message) => Console.Error.WriteLine(message);
}
