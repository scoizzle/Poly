namespace Poly.DslCompiler;

/// <summary>
/// English plural for generated names (route segments, <c>demo.http</c> labels,
/// <c>DbSet</c> properties). Suffix rules cover the shapes domains actually use;
/// the override map covers irregular nouns and uncountables, and a name that
/// already ends in a plural -s is returned unchanged so it is not doubled.
/// </summary>
internal static class Pluralizer {
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase) {
        // Irregular plurals (the plural form maps to itself so it stays put).
        ["person"] = "people",
        ["people"] = "people",
        ["child"] = "children",
        ["children"] = "children",
        ["man"] = "men",
        ["men"] = "men",
        ["woman"] = "women",
        ["women"] = "women",
        ["mouse"] = "mice",
        ["mice"] = "mice",
        ["goose"] = "geese",
        ["geese"] = "geese",
        ["tooth"] = "teeth",
        ["teeth"] = "teeth",
        ["foot"] = "feet",
        ["feet"] = "feet",
        ["datum"] = "data",
        ["criterion"] = "criteria",
        // Uncountable nouns.
        ["data"] = "data",
        ["information"] = "information",
        ["equipment"] = "equipment",
        ["software"] = "software",
    };

    public static string Pluralize(string name) {
        if (string.IsNullOrEmpty(name))
            return name;

        if (Overrides.TryGetValue(name, out var irregular))
            return MatchCase(name, irregular);

        // Already plural: a trailing -s that is not the singular -ss (Class) or
        // -us (Status) shape. `Orders` stays `Orders`, not `Orderses`.
        if (EndsWith(name, "s") && !EndsWith(name, "ss") && !EndsWith(name, "us"))
            return name;

        // Consonant + y -> ies (City -> Cities), vowel + y -> s (Key -> Keys).
        if (EndsWith(name, "y") && name.Length >= 2 && !IsVowel(name[^2]))
            return name[..^1] + "ies";

        // Sibilants take -es (Address -> Addresses, Box -> Boxes, Batch -> Batches).
        if (EndsWith(name, "s") || EndsWith(name, "x") || EndsWith(name, "z")
            || EndsWith(name, "ch") || EndsWith(name, "sh"))
            return name + "es";

        return name + "s";
    }

    private static bool EndsWith(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsVowel(char c) => "aeiou".Contains(char.ToLowerInvariant(c));

    private static string MatchCase(string source, string lower) {
        if (source.All(char.IsUpper))
            return lower.ToUpperInvariant();
        return char.IsUpper(source[0])
            ? char.ToUpperInvariant(lower[0]) + lower[1..]
            : lower;
    }
}