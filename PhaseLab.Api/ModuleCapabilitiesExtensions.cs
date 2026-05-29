namespace PhaseLab.Api;

public static class ModuleCapabilitiesExtensions
{
    public static IReadOnlyList<string> ToCapabilityNames(this ModuleCapabilities capabilities)
    {
        if (capabilities == ModuleCapabilities.None)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (ModuleCapabilities flag in Enum.GetValues<ModuleCapabilities>())
        {
            if (flag == ModuleCapabilities.None)
            {
                continue;
            }

            if (capabilities.HasFlag(flag))
            {
                names.Add(ToKebabCase(flag.ToString()));
            }
        }

        return names;
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var chars = new List<char>(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c) && i > 0)
            {
                chars.Add('-');
            }

            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }
}
