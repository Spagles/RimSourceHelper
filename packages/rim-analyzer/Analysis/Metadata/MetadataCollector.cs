using Mono.Cecil;
using RimAnalyzer.Models;

namespace RimAnalyzer.Analysis.Metadata;

// 从 Cecil AssemblyDefinition 中提取类型/方法/字段/属性元数据
public static class MetadataCollector
{
    public static CollectionResult Collect(IReadOnlyList<AssemblyDefinition> assemblies, Action<string> log)
    {
        var types = new List<TypeMetadata>();
        var methodMap = new Dictionary<MethodDefinition, MethodEntity>();
        var fieldMap = new Dictionary<FieldDefinition, FieldEntity>();

        foreach (var asm in assemblies)
        {
            var assemblyPath = asm.MainModule.FileName;
            log($"[INFO] Scanning assembly: {asm.Name.Name}");

            foreach (var type in asm.MainModule.Types)
                CollectTypeRecursive(type, asm.Name.Name, assemblyPath, types, methodMap, fieldMap);
        }

        log($"[INFO] Collected {types.Count} types, " +
            $"{methodMap.Count} methods, " +
            $"{fieldMap.Count} fields, " +
            $"{types.Sum(t => t.Properties.Count)} properties");

        return new CollectionResult { Types = types, MethodMap = methodMap, FieldMap = fieldMap };
    }

    // 收集单个类型并递归处理嵌套类型
    private static void CollectTypeRecursive(TypeDefinition type, string assemblyName,
        string assemblyPath, List<TypeMetadata> output,
        Dictionary<MethodDefinition, MethodEntity> methodMap,
        Dictionary<FieldDefinition, FieldEntity> fieldMap)
    {
        if (type.Name == "<Module>")
            return;

        var meta = new TypeMetadata
        {
            Type = new TypeEntity
            {
                Namespace = string.IsNullOrEmpty(type.Namespace) ? null : type.Namespace,
                Name = type.Name,
                FullName = type.FullName,
                BaseType = type.BaseType?.FullName,
                IsAbstract = type.IsAbstract,
                IsInterface = type.IsInterface,
                IsEnum = type.IsEnum,
                IsSealed = type.IsSealed,
                Accessibility = GetTypeAccessibility(type),
                AssemblyName = assemblyName,
                AssemblyPath = assemblyPath
            },
            Interfaces = type.Interfaces.Select(i => i.InterfaceType.FullName).ToList()
        };

        CollectMethods(type, meta, methodMap);
        CollectFields(type, meta, fieldMap);
        CollectProperties(type, meta);

        output.Add(meta);

        // 递归收集嵌套类型
        foreach (var nested in type.NestedTypes)
            CollectTypeRecursive(nested, assemblyName, assemblyPath, output, methodMap, fieldMap);
    }

    private static void CollectMethods(TypeDefinition type, TypeMetadata meta,
        Dictionary<MethodDefinition, MethodEntity> methodMap)
    {
        foreach (var method in type.Methods)
        {
            // 跳过事件 add/remove（保留属性 getter/setter 用于引用追踪）
            if (method.IsSpecialName && (method.Name.StartsWith("add_") || method.Name.StartsWith("remove_")))
                continue;

            var isAccessor = method.IsSpecialName &&
                (method.Name.StartsWith("get_") || method.Name.StartsWith("set_"));

            var entity = new MethodEntity
            {
                Name = method.Name,
                FullName = $"{type.FullName}.{method.Name}",
                Signature = BuildSignature(type, method),
                ReturnType = method.ReturnType.FullName,
                IsStatic = method.IsStatic,
                IsVirtual = method.IsVirtual,
                IsAbstract = method.IsAbstract,
                IsAccessor = isAccessor,
                Accessibility = GetMethodAccessibility(method)
            };

            meta.Methods.Add(entity);
            methodMap[method] = entity;
        }
    }

    private static void CollectFields(TypeDefinition type, TypeMetadata meta,
        Dictionary<FieldDefinition, FieldEntity> fieldMap)
    {
        foreach (var field in type.Fields)
        {
            var entity = new FieldEntity
            {
                Name = field.Name,
                FieldType = field.FieldType.FullName,
                IsStatic = field.IsStatic,
                Accessibility = GetFieldAccessibility(field)
            };
            meta.Fields.Add(entity);
            fieldMap[field] = entity;
        }
    }

    private static void CollectProperties(TypeDefinition type, TypeMetadata meta)
    {
        foreach (var prop in type.Properties)
        {
            meta.Properties.Add(new PropertyEntity
            {
                Name = prop.Name,
                PropertyType = prop.PropertyType.FullName,
                HasGetter = prop.GetMethod is not null,
                HasSetter = prop.SetMethod is not null,
                Accessibility = GetPropertyAccessibility(prop)
            });
        }
    }

    private static string BuildSignature(TypeDefinition type, MethodDefinition method)
    {
        var paramTypes = method.Parameters
            .Select(p => FormatTypeName(p.ParameterType))
            .ToArray();
        var returnType = FormatTypeName(method.ReturnType);
        return $"{returnType} {type.FullName}.{method.Name}({string.Join(",", paramTypes)})";
    }

    private static string FormatTypeName(TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType generic)
        {
            var args = string.Join(",", generic.GenericArguments.Select(FormatTypeName));
            return $"{generic.ElementType.FullName}<{args}>";
        }
        if (typeRef.IsByReference)
            return $"ref {FormatTypeName(((ByReferenceType)typeRef).ElementType)}";
        if (typeRef.IsArray)
            return FormatTypeName(((ArrayType)typeRef).ElementType) + "[]";
        return typeRef.FullName;
    }

    // --- Accessibility helpers ---

    private static string? GetTypeAccessibility(TypeDefinition type)
    {
        if (type.IsPublic || type.IsNestedPublic) return "public";
        if (type.IsNestedPrivate) return "private";
        if (type.IsNestedFamily) return "protected";
        if (type.IsNestedAssembly) return "internal";
        if (type.IsNestedFamilyOrAssembly) return "protected internal";
        if (type.IsNestedFamilyAndAssembly) return "private protected";
        return "internal";
    }

    private static string? GetMethodAccessibility(MethodDefinition method)
    {
        if (method.IsPublic) return "public";
        if (method.IsPrivate) return "private";
        if (method.IsFamily) return "protected";
        if (method.IsAssembly) return "internal";
        if (method.IsFamilyOrAssembly) return "protected internal";
        if (method.IsFamilyAndAssembly) return "private protected";
        return "private";
    }

    private static string? GetFieldAccessibility(FieldDefinition field)
    {
        if (field.IsPublic) return "public";
        if (field.IsPrivate) return "private";
        if (field.IsFamily) return "protected";
        if (field.IsAssembly) return "internal";
        if (field.IsFamilyOrAssembly) return "protected internal";
        return "private";
    }

    private static string? GetPropertyAccessibility(PropertyDefinition prop)
    {
        var method = prop.GetMethod ?? prop.SetMethod;
        return method is not null ? GetMethodAccessibility(method) : null;
    }
}
