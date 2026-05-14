using Poly.Data.Modeling;

namespace Poly.DomainModeling.V2;

/// <summary>
/// Immutable result returned after dispatching one or more intents in a session transaction.
/// </summary>
public sealed record DomainTransactionResult(
    bool Succeeded,
    long Revision,
    DomainMutationTrace Trace,
    IReadOnlyList<string> Diagnostics
);
