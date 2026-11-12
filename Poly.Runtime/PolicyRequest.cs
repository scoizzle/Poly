using System.Collections.ObjectModel;

namespace Poly.Runtime;

/// <summary>
/// One request to an <see cref="IPolicyEvaluator"/>: the principal, the entity instance and
/// the policy to decide (2026-11-11 actor/principal ruling, D3). It is the lowered module's
/// policy-body argument, shared by the simulator and printed C#.
/// <para>
/// <see cref="Principal"/> is nullable: with <c>uses auth</c> and no host principal the
/// evaluator receives null and must deny. <see cref="Arguments"/> is copied on construction,
/// so a caller cannot change the shape after hand-off. A null element is legal; the ruling
/// gives the sequence an <c>object?</c> element type.
/// </para>
/// </summary>
public sealed class PolicyRequest {
    /// <summary>The principal to decide for; null when the host supplied none.</summary>
    public Principal? Principal { get; }

    /// <summary>The entity the policy belongs to; never null, empty or whitespace.</summary>
    public string Entity { get; }

    /// <summary>The instance the policy is evaluated against; never null.</summary>
    public object Instance { get; }

    /// <summary>The policy name; never null, empty or whitespace.</summary>
    public string Policy { get; }

    /// <summary>The policy's arguments in declaration order; never null.</summary>
    public IReadOnlyList<object?> Arguments { get; }

    /// <summary>Creates a policy request.</summary>
    /// <param name="principal">The principal, or null when the host supplied none.</param>
    /// <param name="entity">The entity name.</param>
    /// <param name="instance">The instance the policy is evaluated against.</param>
    /// <param name="policy">The policy name.</param>
    /// <param name="arguments">The policy arguments; null means none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/>, <paramref name="instance"/>
    /// or <paramref name="policy"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="entity"/> or <paramref name="policy"/>
    /// is empty or whitespace.</exception>
    public PolicyRequest(Principal? principal, string entity, object instance, string policy,
        IReadOnlyList<object?>? arguments = null) {
        if (entity is null)
            throw new ArgumentNullException(nameof(entity));

        if (string.IsNullOrWhiteSpace(entity))
            throw new ArgumentException("An entity name is required.", nameof(entity));

        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        if (policy is null)
            throw new ArgumentNullException(nameof(policy));

        if (string.IsNullOrWhiteSpace(policy))
            throw new ArgumentException("A policy name is required.", nameof(policy));

        Principal = principal;
        Entity = entity;
        Instance = instance;
        Policy = policy;
        Arguments = CopyArguments(arguments);
    }

    static ReadOnlyCollection<object?> CopyArguments(IReadOnlyList<object?>? arguments) {
        var copy = new List<object?>(arguments?.Count ?? 0);
        if (arguments is not null)
            copy.AddRange(arguments);

        return new ReadOnlyCollection<object?>(copy);
    }
}