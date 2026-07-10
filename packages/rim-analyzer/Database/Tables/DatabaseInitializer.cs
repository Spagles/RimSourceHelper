using Dapper;
using Microsoft.Data.Sqlite;

namespace RimAnalyzer.Database.Tables;

// 数据库初始化：创建所有表和索引
public static class DatabaseInitializer
{
    public static void Initialize(SqliteConnection connection)
    {
        connection.Execute(CreateSourcesSql);
        connection.Execute(CreateTypesSql);
        connection.Execute(CreateMethodsSql);
        connection.Execute(CreateFieldsSql);
        connection.Execute(CreatePropertiesSql);
        connection.Execute(CreateInheritanceSql);
        connection.Execute(CreateCallsSql);
        connection.Execute(CreateFieldAccessesSql);
        connection.Execute(CreateDefsSql);
        connection.Execute(CreateDefReferencesSql);
        connection.Execute(CreateHarmonyPatchesSql);
        connection.Execute(CreateXmlPatchesSql);
        connection.Execute(CreateMetadataSql);
    }

    private const string CreateSourcesSql = """
        CREATE TABLE IF NOT EXISTS Sources (
            Id            INTEGER PRIMARY KEY,
            Name          TEXT NOT NULL UNIQUE,
            Type          TEXT NOT NULL,
            PackageId     TEXT,
            AssemblyPath  TEXT,
            RootPath      TEXT
        );
        """;

    private const string CreateTypesSql = """
        CREATE TABLE IF NOT EXISTS Types (
            Id            INTEGER PRIMARY KEY,
            Namespace     TEXT,
            Name          TEXT NOT NULL,
            FullName      TEXT NOT NULL UNIQUE,
            BaseType      TEXT,
            IsAbstract    INTEGER NOT NULL DEFAULT 0,
            IsInterface   INTEGER NOT NULL DEFAULT 0,
            IsEnum        INTEGER NOT NULL DEFAULT 0,
            IsSealed      INTEGER NOT NULL DEFAULT 0,
            Accessibility TEXT,
            AssemblyName  TEXT,
            AssemblyPath  TEXT,
            SourceId      INTEGER NOT NULL REFERENCES Sources(Id)
        );
        CREATE INDEX IF NOT EXISTS idx_types_name ON Types(Name);
        CREATE INDEX IF NOT EXISTS idx_types_namespace ON Types(Namespace);
        CREATE INDEX IF NOT EXISTS idx_types_source ON Types(SourceId);
        """;

    private const string CreateMethodsSql = """
        CREATE TABLE IF NOT EXISTS Methods (
            Id            INTEGER PRIMARY KEY,
            TypeId        INTEGER NOT NULL REFERENCES Types(Id),
            Name          TEXT NOT NULL,
            FullName      TEXT NOT NULL,
            Signature     TEXT NOT NULL,
            ReturnType    TEXT,
            IsStatic      INTEGER NOT NULL DEFAULT 0,
            IsVirtual     INTEGER NOT NULL DEFAULT 0,
            IsAbstract    INTEGER NOT NULL DEFAULT 0,
            IsAccessor    INTEGER NOT NULL DEFAULT 0,
            Accessibility TEXT,
            SourceId      INTEGER NOT NULL REFERENCES Sources(Id)
        );
        CREATE INDEX IF NOT EXISTS idx_methods_name ON Methods(Name);
        CREATE INDEX IF NOT EXISTS idx_methods_typeid ON Methods(TypeId);
        CREATE INDEX IF NOT EXISTS idx_methods_fullname ON Methods(FullName);
        CREATE INDEX IF NOT EXISTS idx_methods_source ON Methods(SourceId);
        """;

    private const string CreateFieldsSql = """
        CREATE TABLE IF NOT EXISTS Fields (
            Id            INTEGER PRIMARY KEY,
            TypeId        INTEGER NOT NULL REFERENCES Types(Id),
            Name          TEXT NOT NULL,
            FieldType     TEXT,
            IsStatic      INTEGER NOT NULL DEFAULT 0,
            Accessibility TEXT,
            SourceId      INTEGER NOT NULL REFERENCES Sources(Id)
        );
        CREATE INDEX IF NOT EXISTS idx_fields_typeid ON Fields(TypeId);
        """;

    private const string CreatePropertiesSql = """
        CREATE TABLE IF NOT EXISTS Properties (
            Id            INTEGER PRIMARY KEY,
            TypeId        INTEGER NOT NULL REFERENCES Types(Id),
            Name          TEXT NOT NULL,
            PropertyType  TEXT,
            HasGetter     INTEGER NOT NULL DEFAULT 0,
            HasSetter     INTEGER NOT NULL DEFAULT 0,
            Accessibility TEXT,
            SourceId      INTEGER NOT NULL REFERENCES Sources(Id)
        );
        CREATE INDEX IF NOT EXISTS idx_properties_typeid ON Properties(TypeId);
        """;

    private const string CreateInheritanceSql = """
        CREATE TABLE IF NOT EXISTS Inheritance (
            ParentTypeId  INTEGER NOT NULL REFERENCES Types(Id),
            ChildTypeId   INTEGER NOT NULL REFERENCES Types(Id),
            IsInterface   INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (ParentTypeId, ChildTypeId)
        );
        CREATE INDEX IF NOT EXISTS idx_inheritance_parent ON Inheritance(ParentTypeId);
        CREATE INDEX IF NOT EXISTS idx_inheritance_child ON Inheritance(ChildTypeId);
        """;

    private const string CreateCallsSql = """
        CREATE TABLE IF NOT EXISTS Calls (
            CallerMethodId INTEGER NOT NULL REFERENCES Methods(Id),
            CalleeMethodId INTEGER NOT NULL REFERENCES Methods(Id),
            PRIMARY KEY (CallerMethodId, CalleeMethodId)
        );
        CREATE INDEX IF NOT EXISTS idx_calls_caller ON Calls(CallerMethodId);
        CREATE INDEX IF NOT EXISTS idx_calls_callee ON Calls(CalleeMethodId);
        """;

    private const string CreateFieldAccessesSql = """
        CREATE TABLE IF NOT EXISTS FieldAccesses (
            MethodId   INTEGER NOT NULL REFERENCES Methods(Id),
            FieldId    INTEGER NOT NULL REFERENCES Fields(Id),
            AccessType TEXT NOT NULL,
            PRIMARY KEY (MethodId, FieldId, AccessType)
        );
        CREATE INDEX IF NOT EXISTS idx_fieldaccess_method ON FieldAccesses(MethodId);
        CREATE INDEX IF NOT EXISTS idx_fieldaccess_field ON FieldAccesses(FieldId);
        """;

    private const string CreateDefsSql = """
        CREATE TABLE IF NOT EXISTS Defs (
            Id          INTEGER PRIMARY KEY,
            DefName     TEXT,
            DefType     TEXT NOT NULL,
            ParentDef   TEXT,
            Label       TEXT,
            Description TEXT,
            IsAbstract  INTEGER NOT NULL DEFAULT 0,
            RawXml      TEXT NOT NULL,
            SourceFile  TEXT,
            SourceId    INTEGER NOT NULL REFERENCES Sources(Id)
        );
        CREATE INDEX IF NOT EXISTS idx_defs_name ON Defs(DefName);
        CREATE INDEX IF NOT EXISTS idx_defs_type ON Defs(DefType);
        CREATE INDEX IF NOT EXISTS idx_defs_source ON Defs(SourceId);
        """;

    private const string CreateDefReferencesSql = """
        CREATE TABLE IF NOT EXISTS DefReferences (
            SourceDefId   INTEGER NOT NULL REFERENCES Defs(Id),
            TargetDefName TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_defrefs_source ON DefReferences(SourceDefId);
        CREATE INDEX IF NOT EXISTS idx_defrefs_target ON DefReferences(TargetDefName);
        """;

    private const string CreateHarmonyPatchesSql = """
        CREATE TABLE IF NOT EXISTS HarmonyPatches (
            Id            INTEGER PRIMARY KEY,
            TargetType    TEXT NOT NULL,
            TargetMethod  TEXT,
            PatchType     TEXT NOT NULL,
            PatchClass    TEXT NOT NULL,
            PatchMethod   TEXT NOT NULL,
            TargetParams  TEXT,
            Priority      TEXT,
            SourceId      INTEGER NOT NULL REFERENCES Sources(Id)
        );
        CREATE INDEX IF NOT EXISTS idx_harmony_target_type ON HarmonyPatches(TargetType);
        CREATE INDEX IF NOT EXISTS idx_harmony_target_method ON HarmonyPatches(TargetType, TargetMethod);
        CREATE INDEX IF NOT EXISTS idx_harmony_source ON HarmonyPatches(SourceId);
        """;

    private const string CreateXmlPatchesSql = """
        CREATE TABLE IF NOT EXISTS XmlPatches (
            Id               INTEGER PRIMARY KEY,
            SourceId         INTEGER NOT NULL REFERENCES Sources(Id),
            TargetXPaths     TEXT,
            OperationClasses TEXT,
            RawXml           TEXT NOT NULL,
            SourceFile       TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_xmlpatch_source ON XmlPatches(SourceId);
        """;

    private const string CreateMetadataSql = """
        CREATE TABLE IF NOT EXISTS Metadata (
            Key   TEXT PRIMARY KEY,
            Value TEXT
        );
        """;
}
