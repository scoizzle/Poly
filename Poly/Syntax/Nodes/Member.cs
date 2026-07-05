using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a member access operation (property, field, or method access) in an interpretation tree.
/// </summary>
/// <remarks>
/// This operator enables accessing members of a value using dot notation (e.g., <c>person.Name</c>).
/// Member resolution happens in semantic analysis passes (INodeAnalyzer implementations) using type information from the context.
/// </remarks>
public sealed record Member(Node Value, string MemberName) : Expression {
    public override IEnumerable<Node?> Children => [Value];

    /// <inheritdoc />
    public override string ToString() => $"{Value}.{MemberName}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Check for CallSiteIndexMetadata from ANA-004 for portable serialization.
        var siteIndex = context.Analysis.GetCallSiteIndex(this);

        // Check if the member resolves to a CLR property getter or method
        var resolved = context.Analysis.GetResolvedMember(this);
        if (resolved is Introspection.CommonLanguageRuntime.ClrTypeProperty prop) {
            var getter = prop.PropertyInfo.GetGetMethod(nonPublic: true);
            if (getter is not null) {
                if (!prop.IsStatic) {
                    foreach (var p in Value.ToPrimitives(context)) yield return p;
                }
                int argCount = getter.GetParameters().Length + (prop.IsStatic ? 0 : 1);
                yield return new Primitives.CallExternal(getter, argCount, prop.IsStatic, SiteIndex: siteIndex);
                yield break;
            }
        }

        if (resolved is Introspection.CommonLanguageRuntime.ClrMethod method) {
            if (!method.IsStatic) {
                foreach (var p in Value.ToPrimitives(context)) yield return p;
            }
            int argCount = method.MethodInfo.GetParameters().Length + (method.IsStatic ? 0 : 1);
            yield return new Primitives.CallExternal(method.MethodInfo, argCount, method.IsStatic, SiteIndex: siteIndex);
            yield break;
        }

        // Unresolved member: passthrough the value with a zero placeholder
        foreach (var p in Value.ToPrimitives(context)) yield return p;
        yield return new Primitives.PushConstant(0L);
    }
}