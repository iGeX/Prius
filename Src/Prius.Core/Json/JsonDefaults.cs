using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prius.Core.Json;

public static class JsonDefaults
{
    public static JsonSerializerOptions ConfigureJsonSerializerOptions(this JsonSerializerOptions options)
    {
        options.TypeInfoResolver = new PocoModelTypeInfoResolver();
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.Converters.Add(new JsonStringEnumConverter());
        options.WriteIndented = true;
        options.PropertyNamingPolicy = null;
        
        return options;
    }

    public static JsonSerializerOptions Options { get; set; } = ConfigureJsonSerializerOptions(new JsonSerializerOptions());
}
