using System.Collections.ObjectModel;

namespace Poly.Runtime;

/// <summary>
/// The host-supplied principal a policy evaluator decides for. It is not a domain type,
/// not a contract <c>value</c> and not a DSL term: the host supplies it and a library
/// types it (2026-11-11 actor/principal ruling, D1).
/// <para>
/// Every collection is copied on construction, so the host cannot change a principal after
/// handing it over and the exposed collections reject mutation. <see cref="Roles"/> keeps
/// the host's order and is not deduplicated. <see cref="Claims"/> is keyed ordinally and
/// case-sensitively; a caller's own comparer is not carried over.
/// </para>
/// </summary>
public sealed class Principal {
    /// <summary>The host's identity for this principal; never null, empty or whitespace.</summary>
    public string Id { get; }

    /// <summary>The principal's roles, in the order the host supplied them; never null.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>The principal's claims, keyed ordinally; never null.</summary>
    public IReadOnlyDictionary<string, string> Claims { get; }

    /// <summary>Creates a principal from the host's identity, roles and claims.</summary>
    /// <param name="id">The host identity. A blank identity is rejected: a host with no
    /// identity passes a null principal to the evaluator instead of a half principal.</param>
    /// <param name="roles">The host roles; null means no roles.</param>
    /// <param name="claims">The host claims; null means no claims.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null, empty or whitespace;
    /// <paramref name="roles"/> holds a null element; or <paramref name="claims"/> holds a null
    /// value.</exception>
    public Principal(string id, IReadOnlyList<string>? roles = null,
        IReadOnlyDictionary<string, string>? claims = null) {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A principal id is required.", nameof(id));

        Id = id;
        Roles = CopyRoles(roles);
        Claims = CopyClaims(claims);
    }

    static ReadOnlyCollection<string> CopyRoles(IReadOnlyList<string>? roles) {
        var copy = new List<string>(roles?.Count ?? 0);
        if (roles is not null) {
            foreach (var role in roles) {
                if (role is null)
                    throw new ArgumentException("A role element must not be null.", nameof(roles));

                copy.Add(role);
            }
        }

        return new ReadOnlyCollection<string>(copy);
    }

    static ReadOnlyDictionary<string, string> CopyClaims(IReadOnlyDictionary<string, string>? claims) {
        var copy = new Dictionary<string, string>(claims?.Count ?? 0, StringComparer.Ordinal);
        if (claims is not null) {
            foreach (var claim in claims) {
                if (claim.Value is null)
                    throw new ArgumentException("A claim value must not be null.", nameof(claims));

                // An ordinal key is added as-is; a duplicate can only come from a source
                // dictionary that is not ordinal itself, and that is a caller bug.
                copy.Add(claim.Key, claim.Value);
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}