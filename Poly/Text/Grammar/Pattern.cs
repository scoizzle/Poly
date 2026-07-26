namespace Poly.Text.Grammar;

/// <summary>
/// A named pattern: a sequence of <see cref="IPatternElement{TKind}"/> that
/// describes a valid construct in the grammar. Patterns are registered under
/// named rules in a <see cref="Grammar{TKind}"/> and matched by <see cref="Matcher{TKind}"/>.
/// </summary>
public sealed class Pattern<TKind> where TKind : struct {
    /// <summary>Descriptive name used in match results and error messages.</summary>
    public string Name { get; }

    /// <summary>The ordered sequence of elements this pattern matches.</summary>
    public IReadOnlyList<IPatternElement<TKind>> Elements { get; }

    public Pattern(string name, IReadOnlyList<IPatternElement<TKind>> elements) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
    }
}