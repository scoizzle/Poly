namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// The current clock instant (UTC). Platform-agnostic clock IR; the CLR host
/// lowers it to <c>DateTime.UtcNow</c> and the store/preprocess path resolves it
/// via an injectable clock (p1 T3).
/// </summary>
public sealed record Now : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}

/// <summary>
/// Today's date (UTC). Platform-agnostic clock IR; the CLR host lowers it to
/// <c>DateOnly.FromDateTime(DateTime.UtcNow)</c>.
/// </summary>
public sealed record Today : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}