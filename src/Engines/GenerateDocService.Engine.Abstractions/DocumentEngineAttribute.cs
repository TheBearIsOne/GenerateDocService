namespace GenerateDocService.Engine.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DocumentEngineAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string[] InputFormats { get; init; } = [];

    public string[] OutputFormats { get; init; } = [];

    public string[] TemplateFormats { get; init; } = [];

    public int Priority { get; init; }
}
