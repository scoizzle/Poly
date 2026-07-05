using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents instance creation by selecting and invoking a constructor on a type.
/// </summary>
/// <remarks>
/// The <see cref="Type"/> is structural and is typically a <see cref="TypeReference"/> or
/// <see cref="TypeDefinitionReference"/>. Constructor resolution happens in semantic analysis
/// passes using the resolved argument and target type information.
/// </remarks>
/// <param name="Type">The type being instantiated.</param>
/// <param name="Arguments">The constructor arguments.</param>
public sealed record New(Node Type, params Node[] Arguments) : Expression {
    public override IEnumerable<Node?> Children => [Type, .. Arguments];

    public override string ToString() => $"new {Type}({string.Join(", ", Arguments)})";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Check for CallSiteIndexMetadata from ANA-004 for portable serialization
        var siteIndex = context.Analysis.GetCallSiteIndex(this);

        if (siteIndex.HasValue) {
            // Catalog entry available: emit CallExternal with indexed constructor.
            // The catalog stores the resolved MethodInfo; the compiler will resolve
            // from the catalog at compile time.
            var resolved = context.Analysis.GetResolvedMember(this);
            if (resolved is Introspection.CommonLanguageRuntime.ClrConstructor ctor) {
                foreach (var arg in Arguments)
                    foreach (var p in arg.ToPrimitives(context)) yield return p;
                int argCount = ctor.ConstructorInfo.GetParameters().Length;
                yield return new Primitives.CallExternal(ctor.ConstructorInfo, argCount, false, siteIndex);
                yield break;
            }
        }

        // Fallback (no catalog metadata): emit Call(argCount, funcIndex: 0)
        foreach (var arg in Arguments)
            foreach (var p in arg.ToPrimitives(context)) yield return p;
        yield return new Primitives.Call(Arguments.Length, 0);
    }
}