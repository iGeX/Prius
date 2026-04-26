using System.Text.Json.Serialization;

namespace Prius.Core.Packages.Registry;

public record ServiceIndexDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("resources")] List<ServiceResourceDto> Resources);

public record ServiceResourceDto(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("@type")] string Type,
    [property: JsonPropertyName("comment")] string Comment,
    [property: JsonPropertyName("clientVersion")] string ClientVersion);

public record RegistrationRootDto(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("items")] List<RegistrationPageDto> Items);

public record RegistrationPageDto(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("items")] List<RegistrationLeafDto> Items,
    [property: JsonPropertyName("lower")] string Lower,
    [property: JsonPropertyName("upper")] string Upper);

public record RegistrationLeafDto(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("packageContent")] string PackageContent,
    [property: JsonPropertyName("catalogEntry")] CatalogEntryDto CatalogEntry);

public record CatalogEntryDto(
    [property: JsonPropertyName("@id")] string Id,
    [property: JsonPropertyName("id")] string PackageId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("authors")] string Authors,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("dependencyGroups")] List<DependencyGroupDto> DependencyGroups);

public record DependencyGroupDto(
    [property: JsonPropertyName("targetFramework")] string TargetFramework,
    [property: JsonPropertyName("dependencies")] List<DependencyDto> Dependencies);

public record DependencyDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("range")] string Range);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ServiceIndexDto))]
[JsonSerializable(typeof(RegistrationRootDto))]
[JsonSerializable(typeof(List<string>))]
internal partial class RegistryJsonContext : JsonSerializerContext;
