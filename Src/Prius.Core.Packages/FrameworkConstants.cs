namespace Prius.Core.Packages;

public static class FrameworkConstants
{
    private static readonly Dictionary<string, string[]> Compatibility = new(StringComparer.OrdinalIgnoreCase)
    {
        ["net10.0"] = ["net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0", "netstandard2.1", "netstandard2.0", "any"],
        ["net8.0"] = ["net8.0", "net7.0", "net6.0", "net5.0", "netstandard2.1", "netstandard2.0", "any"],
        ["netstandard2.1"] = ["netstandard2.1", "netstandard2.0", "any"],
        ["netstandard2.0"] = ["netstandard2.0", "any"]
    };

    public static string[] GetCompatible(string tfm) => Compatibility.TryGetValue(tfm, out var supported) ? supported : [tfm, "any"];
}
