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

    /// <summary>Higher wins a longest-match tie. Default 0.</summary>
    public int Priority { get; }

    /// <summary>
    /// Handler-owned meaning bound at <c>Commit</c>. The engine does not interpret
    /// this; names stay diagnostic labels.
    /// </summary>
    public object? Payload { get; }

    /// <summary>
    /// Declaration order across the builder that registered it (continued by <c>ToBuilder</c> and <c>Extend</c>).
    /// A failure report sorts its expected set by (<see cref="Order"/>, element index), never by the order the
    /// matcher evaluated rows in, which follows the sorted pattern list and the memo.
    /// </summary>
    internal int Order { get; init; }

    public Pattern(
        string name,
        IReadOnlyList<IPatternElement<TToken, TTokenKind>> elements,
        int priority = 0,
        object? payload = null) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        Priority = priority;
        Payload = payload;
    }

    public Pattern<TToken, TTokenKind> WithPayload(object payload) {
        ArgumentNullException.ThrowIfNull(payload);
        return new Pattern<TToken, TTokenKind>(Name, Elements, Priority, payload) { Order = Order };
    }
}