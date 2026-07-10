namespace RimAnalyzer.Models;

// 对应 Types 表的实体模型
public class TypeEntity
{
    public long Id { get; set; }
    public string? Namespace { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? BaseType { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsInterface { get; set; }
    public bool IsEnum { get; set; }
    public bool IsSealed { get; set; }
    public string? Accessibility { get; set; }
    public string? AssemblyName { get; set; }
    public string? AssemblyPath { get; set; }
    public long SourceId { get; set; }
}
