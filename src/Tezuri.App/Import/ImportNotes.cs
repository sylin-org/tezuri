using System.Text.Json.Serialization;

namespace Tezuri.Import;

/// <summary>What the HTML converter changed on its way to Markdown, and what it could not keep.</summary>
public sealed record ImportTransformationV1(
    string Kind,
    string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourcePointer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultPointer);

public sealed record ImportWarningV1(
    string Code,
    string Severity,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourcePointer);
