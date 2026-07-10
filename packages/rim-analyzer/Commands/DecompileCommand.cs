using System.CommandLine;
using System.Text.Json;
using Dapper;
using RimAnalyzer.Analysis.Decompiler;
using RimAnalyzer.Database;
using RimAnalyzer.Models;

namespace RimAnalyzer.Commands;

// decompile 子命令：按需反编译类型或方法，自动从 DB 定位目标 DLL
public static class DecompileCommand
{
    public static Command Create()
    {
        var targetOption = new Option<string>("--target")
        {
            Description = "Type full name, method full name, or method signature",
            Required = true
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Database path",
            Required = true
        };

        var gamePathOption = new Option<string>("--game-path")
        {
            Description = "RimWorld game root (override stored path)"
        };

        var command = new Command("decompile", "Decompile a type or method on demand")
        {
            targetOption,
            dbOption,
            gamePathOption
        };

        command.SetAction((parseResult, _) =>
        {
            var target = parseResult.GetValue(targetOption)!;
            var dbPath = parseResult.GetValue(dbOption)!;
            var gamePath = parseResult.GetValue(gamePathOption);

            try
            {
                var result = Execute(target, dbPath, gamePath);
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    status = "error",
                    error = ex.Message
                }));
                Environment.ExitCode = 1;
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static object Execute(string target, string dbPath, string? gamePathOverride)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"Database not found: {dbPath}");

        using var db = DatabaseContext.Open(dbPath, force: false);

        // 解析游戏路径（参数优先，否则从 Metadata 读取）
        var gamePath = gamePathOverride ?? db.Metadata.Get("game_path")
            ?? throw new InvalidOperationException("Game path not available. Pass --game-path or rebuild database.");
        var managedDir = Path.Combine(gamePath, "RimWorldWin64_Data", "Managed");

        // 尝试作为类型查找
        var type = db.Types.FindByFullName(target);
        if (type is not null)
            return DecompileAsType(db, type, managedDir);

        // 尝试作为方法 FullName 查找
        var methods = db.Connection.Query<MethodEntity>(
            "SELECT * FROM Methods WHERE FullName = @target", new { target }).ToList();
        if (methods.Count > 0)
            return DecompileAsMethod(db, methods, managedDir);

        // 尝试作为方法 Signature 精确查找
        var method = db.Connection.QueryFirstOrDefault<MethodEntity>(
            "SELECT * FROM Methods WHERE Signature = @target", new { target });
        if (method is not null)
            return DecompileAsMethod(db, [method], managedDir);

        throw new InvalidOperationException($"Target not found in database: {target}");
    }

    private static object DecompileAsType(DatabaseContext db, TypeEntity type, string managedDir)
    {
        var assemblyPath = type.AssemblyPath
            ?? throw new InvalidOperationException($"No assembly path for type '{type.FullName}'. Rebuild the database.");
        var refPaths = BuildReferencePaths(assemblyPath, managedDir);

        var code = DecompilerService.DecompileType(assemblyPath, refPaths, type.FullName);

        return new { status = "success", kind = "type", name = type.FullName, source = code };
    }

    private static object DecompileAsMethod(DatabaseContext db, List<MethodEntity> methods, string managedDir)
    {
        // 所有重载应属于同一类型
        var firstMethod = methods[0];
        var type = db.Connection.QueryFirstOrDefault<TypeEntity>(
            "SELECT * FROM Types WHERE Id = @TypeId", new { firstMethod.TypeId })
            ?? throw new InvalidOperationException("Parent type not found in database.");

        var assemblyPath = type.AssemblyPath
            ?? throw new InvalidOperationException($"No assembly path for type '{type.FullName}'. Rebuild the database.");
        var refPaths = BuildReferencePaths(assemblyPath, managedDir);

        var code = DecompilerService.DecompileMethod(assemblyPath, refPaths, type.FullName, firstMethod.Name);

        return new
        {
            status = "success",
            kind = "method",
            name = firstMethod.FullName,
            overloads = methods.Count,
            source = code
        };
    }

    private static string[] BuildReferencePaths(string assemblyPath, string managedDir)
    {
        var paths = new List<string> { managedDir };
        var assemblyDir = Path.GetDirectoryName(assemblyPath);
        if (assemblyDir is not null && assemblyDir != managedDir)
            paths.Add(assemblyDir);
        return paths.ToArray();
    }
}
