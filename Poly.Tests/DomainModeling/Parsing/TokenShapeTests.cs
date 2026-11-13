using System.Reflection;

using Poly.DomainModeling.Language;
using Poly.Grammar;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// The token shape ratchet: a production token carries a kind and a source range, not a string. G-13b deleted
/// <c>DslToken.Text</c>, the last string on a DSL token. <c>CSharpToken.Text</c> is the one allowed exception
/// until G-14 moves the C# reader onto span tokens and deletes this entry; G-16 extends the rule to the test grammars.
/// </summary>
public sealed class TokenShapeTests {
    private static readonly HashSet<string> AllowedStrings = new(StringComparer.Ordinal) {
        "CSharpToken.Text",
    };

    private static IEnumerable<string> StringMembers(Assembly assembly) {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetTypes()) {
            if (!type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IToken<>)))
                continue;
            foreach (var property in type.GetProperties(all).Where(p => p.PropertyType == typeof(string)))
                yield return $"{type.Name}.{property.Name}";
            foreach (var field in type.GetFields(all).Where(f => f.FieldType == typeof(string) && !f.Name.StartsWith('<')))
                yield return $"{type.Name}.{field.Name}";
        }
    }

    [Test]
    public async Task NoProductionTokenCarriesAString() {
        var found = StringMembers(typeof(DslToken).Assembly).Order(StringComparer.Ordinal).ToList();

        await Assert.That(found.Where(m => !AllowedStrings.Contains(m)).ToList()).IsEmpty();
    }

    [Test]
    public async Task NoProductionTokenCarriesAString_AllowListHasNoStaleEntry() {
        var found = StringMembers(typeof(CSharpToken).Assembly).ToHashSet(StringComparer.Ordinal);

        await Assert.That(AllowedStrings.Where(a => !found.Contains(a)).ToList()).IsEmpty();
    }
}