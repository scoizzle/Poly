using Poly.DomainModeling.Evolution;

namespace Poly.DomainModeling.Ontology.Bootstrap;

/// <summary>
/// Entry point for constructing bootstrapped domain models.
///
/// <c>DomainFactory.Create("Orders")</c> returns a domain pre-populated with
/// the canonical built-in primitive types (Boolean, Number, Text, Date, etc.)
/// and valid analysis state.
///
/// This is the primary bootstrap surface for tests, MCP sessions, and direct API consumers.
/// Workspace/session management lives in MCP, not in this type.
/// </summary>
public static class DomainFactory {
    /// <summary>
    /// Creates a new domain with the given name and the canonical built-in primitive types.
    /// </summary>
    /// <param name="name">The domain name.</param>
    /// <returns>A bootstrapped domain with built-in types and clean analysis.</returns>
    public static Domain Create(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ApplyBuiltins(new Domain(name, []) { Extensions = [.. ExtensionCatalog.ProductLanguage] });
    }

    /// <summary>
    /// Creates a new domain with the given name, canonical built-in types,
    /// and additional domain changes applied after bootstrap.
    /// </summary>
    /// <param name="name">The domain name.</param>
    /// <param name="additionalChanges">Additional changes to apply after bootstrapping built-ins.</param>
    /// <returns>A bootstrapped domain with built-in types and additional changes applied.</returns>
    public static Domain Create(string name, params DomainChange[] additionalChanges) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var withBuiltins = ApplyBuiltins(new Domain(name, []) { Extensions = [.. ExtensionCatalog.ProductLanguage] });

        if (additionalChanges.Length == 0)
            return withBuiltins;

        // Apply only the additional changes on top of the builtins-bearing root
        var result = new DomainEvolution(withBuiltins).Apply(additionalChanges);
        return result.Succeeded ? result.Root : withBuiltins;
    }

    /// <summary>
    /// Creates a new domain with the given name, canonical built-in types,
    /// and additional evolution builder configuration applied after bootstrap.
    ///
    /// Built-in types are applied in a first evolution pass, then the
    /// <paramref name="configure"/> callback runs in a second pass. If the
    /// configure pass fails analysis, the result retains built-in types
    /// (the failed changes are discarded). Callers that need failure
    /// diagnostics should use <c>new DomainEvolution(domain).Evolve()</c>
    /// directly instead.
    /// </summary>
    public static Domain Create(string name, Func<EvolutionBuilder, EvolutionBuilder> configure) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var withBuiltins = ApplyBuiltins(new Domain(name, []) { Extensions = [.. ExtensionCatalog.ProductLanguage] });

        var evo = new DomainEvolution(withBuiltins);
        var builder = evo.Evolve();
        builder = configure(builder);
        var result = builder.Apply();

        return result.Succeeded ? result.Root : withBuiltins;
    }

    private static Domain ApplyBuiltins(Domain empty) {
        var changes = CanonicalBuiltInTypeCatalog.CreateChanges();
        var result = new DomainEvolution(empty).Apply(changes);
        return result.Succeeded
            ? result.Root
            : empty; // Fallthrough: builtins should never fail, but be safe
    }
}