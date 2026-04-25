namespace Poly.Data.Modeling.TypeSystem;

/// <summary>
/// Represents a domain, which is a logical grouping of related types and objects.
/// </summary>
public interface IDomainObject {
    /// <summary>
    /// Gets the domain to which this object belongs.
    /// </summary>
    public Domain Domain { get; }
}

public static class IDomainObjectExtensions {
    public static void ThrowIfNullOrMismatchedDomain([NotNull] this IDomainObject? domainObject, Domain other, [CallerArgumentExpression("domainObject")] string paramName = "") {
        if (domainObject is null) {
            throw new ArgumentNullException(paramName);
        }

        if (!ReferenceEquals(domainObject.Domain, other)) {
            throw new InvalidOperationException("Domain objects must belong to the same domain.");
        }
    }
}