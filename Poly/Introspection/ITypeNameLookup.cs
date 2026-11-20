namespace Poly.Introspection;

/// <summary>
/// The name lookup a declaration resolves its own member type references through (#998 T12b). The analysis layer is
/// one, because a declaration resolves against the scope it was declared into; a domain's synthetic declarations and a
/// hand-built test model are others. It is deliberately not <c>Resolve</c> on a system: the thing a declaration asks is
/// "what does this name mean here", and only the owning scope can answer it.
/// </summary>
internal interface ITypeNameLookup {
    /// <summary>The definition <paramref name="name"/> means in this scope, or null.</summary>
    ITypeDefinition? Lookup(string name);
}