namespace Poly.Grammar;

/// <summary>
/// A named pattern: an ordered sequence of elements describing a valid construct.
/// Registered under named rules in a <see cref="Grammar{TToken,TTokenKind}"/> and
/// matched by <see cref="Matcher{TToken,TTokenKind}"/>.
/// </summary>
public sealed class Pattern<TToken, TTokenKind>
    where TToken : struct, IToken<TTokenKind>
    where TTokenKind : struct {
    public string Name { get; }

    public IReadOnlyList<IPatternElement<TToken, TTokenKind>> Elements { get; }

    public Pattern(string name, IReadOnlyList<IPatternElement<TToken, TTokenKind>> elements) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
    }
}