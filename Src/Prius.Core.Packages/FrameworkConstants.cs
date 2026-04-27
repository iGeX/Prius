namespace Prius.Core.Packages;

public static class FrameworkConstants
{
    public const string Any = "any";
    
    private static readonly Dictionary<string, string[]> Compatibility = new(StringComparer.OrdinalIgnoreCase)
    {
        ["net10.0"] = ["net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0", "netstandard2.1", "netstandard2.0", Any],
        ["net8.0"] = ["net8.0", "net7.0", "net6.0", "net5.0", "netstandard2.1", "netstandard2.0", Any],
        ["netstandard2.1"] = ["netstandard2.1", "netstandard2.0", Any],
        ["netstandard2.0"] = ["netstandard2.0", Any]
    };

    public static string[] GetCompatible(string tfm) => Compatibility.TryGetValue(tfm, out var supported) ? supported : [tfm, Any];
}
