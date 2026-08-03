using System.CommandLine;
using System.Text.Json;
using Mono.Cecil;
using RimAnalyzer.Analysis;
using RimAnalyzer.Analysis.CallGraph;
using RimAnalyzer.Analysis.Defs;
using RimAnalyzer.Analysis.Harmony;
using RimAnalyzer.Analysis.Metadata;
using RimAnalyzer.Database;
using RimAnalyzer.Models;

namespace RimAnalyzer.Commands;

// add-mod 子命令：将 Mod 的代码和 Defs 增量添加到已有数据库
public static class AddModCommand
{
    public static Command Create()
    {
        var modPathOption = new Option<string>("--mod-path")
        {
            Description = "Mod root directory",
            Required = true
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Existing database path",
            Required = true
        };

        var gamePathOption = new Option<string>("--game-path")
        {
            Description = "RimWorld game root directory (for reference DLLs)",
            Required = true
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable verbose logging"
        };

        var command = new Command("add-mod", "Add a mod's code and defs to existing database")
        {
            modPathOption,
            dbOption,
            gamePathOption,
            verboseOption
        };

        command.SetAction((parseResult, _) =>
        {
            var options = new AddModOptions
            {
                ModPath = parseResult.GetValue(modPathOption)!,
                Database = parseResult.GetValue(dbOption)!,
                GamePath = parseResult.GetValue(gamePathOption)!,
                Verbose = parseResult.GetValue(verboseOption)
            };

            try
            {
                var result = Execute(options);
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                Console.WriteLine(JsonSerializer.Serialize(
                    new BuildResult { Status = "error", Error = ex.Message }));
                Environment.ExitCode = 1;
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static BuildResult Execute(AddModOptions options)
    {
        // 解析 Mod 目录
        var mod = new ModResolver(options.ModPath);

        // 打开已有数据库
        var gameResolver = new GamePathResolver(options.GamePath);
        var gameVersion = gameResolver.DetectGameVersion();
        mod.Resolve(gameVersion);
        Log($"[INFO] Mod: {mod.Name} (packageId={mod.PackageId}, version={gameVersion})");
        Log($"[INFO] Found {mod.AssemblyPaths.Count} DLLs, {mod.DefFiles.Count} XML files.");
        if (mod.AssemblyPaths.Count > 0)
            gameResolver.Validate();

        using var db = DatabaseContext.Open(options.Database, force: false);

        // 如果已存在同名 Source，先清除旧数据（事务保护，幂等操作）
        var existing = db.Sources.FindByName(mod.Name);
        if (existing is not null)
        {
            Log($"[INFO] Source '{mod.Name}' already exists, replacing...");
            RemoveSourceData(db, existing.Id);
        }

        // 创建 Source 记录
        var modSource = new SourceEntity
        {
            Name = mod.Name,
            Type = "mod",
            PackageId = mod.PackageId,
            AssemblyPath = mod.AssemblyPaths.FirstOrDefault(),
            RootPath = mod.ModRoot
        };
        var sourceId = db.Sources.Insert(modSource);
        Log($"[INFO] Created source: {modSource.Name} (id={sourceId})");

        int typesCount = 0, methodsCount = 0, callCount = 0;

        // 代码分析（仅当 Mod 有 DLL 时）
        if (mod.AssemblyPaths.Count > 0)
        {
            var assemblies = AssemblyLoader.Load(
                mod.AssemblyPaths.ToArray(),
                [gameResolver.ManagedDir],
                Log);

            try
            {
                if (assemblies.Count > 0)
                {
                    var collection = MetadataCollector.Collect(assemblies, Log);
                    var writeResult = MetadataWriter.Write(db, collection.Types, sourceId, Log);
                    typesCount = writeResult.Types;
                    methodsCount = writeResult.Methods;

                    // 构建 MethodDefinition → Id 映射
                    var sigToId = db.Methods.GetSignatureToIdMap();
                    var methodDefToId = new Dictionary<MethodDefinition, long>();
                    foreach (var (def, entity) in collection.MethodMap)
                    {
                        if (sigToId.TryGetValue(entity.Signature, out var id))
                            methodDefToId[def] = id;
                    }
                    Log($"[INFO] Built method mapping: {methodDefToId.Count} entries.");

                    var fieldSigToId = db.Fields.GetSignatureToIdMap();
                    var fieldDefToId = new Dictionary<FieldDefinition, long>();
                    foreach (var (def, entity) in collection.FieldMap)
                    {
                        var sig = $"{def.DeclaringType.FullName}.{def.Name}";
                        if (fieldSigToId.TryGetValue(sig, out var id))
                            fieldDefToId[def] = id;
                    }
                    Log($"[INFO] Built field mapping: {fieldDefToId.Count} entries.");

                    var graphResult = CallGraphAnalyzer.Analyze(assemblies, methodDefToId, fieldDefToId, Log);
                    callCount = CallGraphWriter.Write(db, graphResult.Calls, Log);
                    if (graphResult.FieldAccesses.Count > 0)
                        db.FieldAccesses.BulkInsert(graphResult.FieldAccesses);

                    // Harmony Patch 分析
                    var patches = HarmonyAnalyzer.Analyze(assemblies, Log);
                    foreach (var p in patches) p.SourceId = sourceId;
                    if (patches.Count > 0)
                    {
                        var patchCount = db.HarmonyPatches.BulkInsert(patches);
                        Log($"[INFO] Inserted {patchCount} Harmony patches.");
                    }
                }
            }
            finally
            {
                foreach (var asm in assemblies)
                    asm.Dispose();
            }
        }

        // Defs 解析（始终执行）
        var modDefs = DefParser.ParseFiles(mod.DefFiles, mod.ModRoot, sourceId, Log);
        var defResult = DefWriter.Write(db, modDefs, Log);

        // XML Patches 解析
        var xmlPatches = XmlPatchParser.ParseFiles(mod.PatchFiles, mod.ModRoot, sourceId, Log);
        if (xmlPatches.Count > 0)
        {
            var xmlPatchCount = db.XmlPatches.BulkInsert(xmlPatches);
            Log($"[INFO] Inserted {xmlPatchCount} XML patches.");
        }

        Log("[INFO] Mod added successfully.");

        return new BuildResult
        {
            Status = "success",
            Types = typesCount,
            Methods = methodsCount,
            Calls = callCount,
            Defs = defResult.Defs
        };
    }

    // 删除指定 Source 的所有关联数据（事务保护，原子操作）
    private static void RemoveSourceData(DatabaseContext db, long sourceId)
    {
        var conn = db.Connection;
        using var transaction = conn.BeginTransaction();
        Dapper.SqlMapper.Execute(conn, "DELETE FROM XmlPatches WHERE SourceId = @sourceId", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM HarmonyPatches WHERE SourceId = @sourceId", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM Calls WHERE CallerMethodId IN (SELECT Id FROM Methods WHERE SourceId = @sourceId) OR CalleeMethodId IN (SELECT Id FROM Methods WHERE SourceId = @sourceId)", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM FieldAccesses WHERE MethodId IN (SELECT Id FROM Methods WHERE SourceId = @sourceId) OR FieldId IN (SELECT Id FROM Fields WHERE SourceId = @sourceId)", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM Inheritance WHERE ParentTypeId IN (SELECT Id FROM Types WHERE SourceId = @sourceId) OR ChildTypeId IN (SELECT Id FROM Types WHERE SourceId = @sourceId)", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM DefReferences WHERE SourceDefId IN (SELECT Id FROM Defs WHERE SourceId = @sourceId)", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM Properties WHERE SourceId = @sourceId", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM Fields WHERE SourceId = @sourceId", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM Methods WHERE SourceId = @sourceId", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM Types WHERE SourceId = @sourceId", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM Defs WHERE SourceId = @sourceId", new { sourceId }, transaction);
        Dapper.SqlMapper.Execute(conn, "DELETE FROM Sources WHERE Id = @sourceId", new { sourceId }, transaction);
        transaction.Commit();
    }

    private static void Log(string message) => Console.Error.WriteLine(message);
}
