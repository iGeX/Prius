namespace Prius.Core.Json;

public sealed class PocoModelTypeInfoResolver : DefaultJsonTypeInfoResolver
{
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var jsonTypeInfo = base.GetTypeInfo(type, options);

        if (!typeof(IPocoModel).IsAssignableFrom(jsonTypeInfo.Type)) 
            return jsonTypeInfo;
        
        var result = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type",
            IgnoreUnrecognizedTypeDiscriminators = false,
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
        };

        foreach (var derivedType in AppDomain.CurrentDomain.GetAssemblies()
                     .SelectMany(a => a.GetExportedTypes()
                         .Where(t => t is { IsAbstract: false, IsGenericType: false } && t != jsonTypeInfo.Type && jsonTypeInfo.Type.IsAssignableFrom(t)))
                     .Select(t => new JsonDerivedType(t,  $"{t.FullName}, {t.Assembly.GetName().Name}")))
            result.DerivedTypes.Add(derivedType);

        if (result.DerivedTypes.Count > 0)
            jsonTypeInfo.PolymorphismOptions = result;

        return jsonTypeInfo;
    }
}
