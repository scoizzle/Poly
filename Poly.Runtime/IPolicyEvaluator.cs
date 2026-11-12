namespace Poly.Runtime;

/// <summary>
/// Decides whether a policy allows an action for a principal. The lowered module's policy
/// body calls it, so the simulator and printed C# share one call site (2026-11-11
/// actor/principal ruling, D3; the call site itself is F-26).
/// <para>
/// <c>Success(true)</c> allows and <c>Success(false)</c> denies. A failure means the
/// evaluator cannot decide, and the guard wraps it as
/// <see cref="DomainFailureKind.Policy"/>. A null <see cref="PolicyRequest.Principal"/> is a
/// legal request, and an evaluator must deny it rather than allow it: there is no default
/// evaluator and no allow-all fallback in this contract.
/// </para>
/// <para>
/// The member is synchronous. F-31/F-32's awaited host effect replaces it, which is a
/// scheduled supersession and needs an ADR amendment first.
/// </para>
/// </summary>
public interface IPolicyEvaluator {
    /// <summary>Decides the request's policy for the request's principal.</summary>
    /// <returns>A <see cref="DomainResult{T}"/> of <see cref="bool"/>: true allows, false
    /// denies, a failure means the evaluator cannot decide.</returns>
    DomainResult<bool> Evaluate(PolicyRequest request);
}