using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a method invocation operation in an interpretation tree.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Delegate"/> is a structural reference to the method being called — typically a
/// <see cref="Member"/> node (e.g. <c>target.MethodName</c>) resolved by
/// semantic analysis passes.
/// </para>
/// <para>
/// Using a node as the method reference (rather than a bare string) makes call sites
/// structurally consistent with all other node references, allows the same resolution
/// infrastructure to handle both property reads and method calls, and makes the intent
/// explicit in the tree.
/// </para>
/// </remarks>
/// <param name="Delegate">The method reference node, typically a <see cref="Member"/>.</param>
/// <param name="Arguments">The arguments to pass to the method.</param>
public sealed record Invoke(Node Delegate, params Node[] Arguments) : Expression {
    public override IEnumerable<Node?> Children => [Delegate, .. Arguments];

    public override string ToString() => $"{Delegate}({string.Join(", ", Arguments)})";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // Check resolved member metadata for CLR method dispatch
        var resolved = context.GetResolvedMember(this);

        if (resolved is Poly.Introspection.CommonLanguageRuntime.ClrMethod clrMethod) {
            // CLR method call: emit instance (if non-static) then args, then CallExternal
            if (!clrMethod.IsStatic) {
                // Delegate is a Member node — emit the instance value
                if (Delegate is Member member) {
                    foreach (var p in member.Value.ToPrimitives(context))
                        yield return p;
                }
                else {
                    foreach (var p in Delegate.ToPrimitives(context))
                        yield return p;
                }
            }
            foreach (var arg in Arguments)
                foreach (var p in arg.ToPrimitives(context)) yield return p;
            int argCount = clrMethod.MethodInfo.GetParameters().Length + (clrMethod.IsStatic ? 0 : 1);
            yield return new Poly.Syntax.Primitives.CallExternal(clrMethod.MethodInfo, argCount, clrMethod.IsStatic);
            yield break;
        }

        if (resolved is Poly.Introspection.CommonLanguageRuntime.ClrTypeProperty clrProp) {
            // Property getter: emit instance, then CallExternal for getter
            var getter = clrProp.PropertyInfo.GetGetMethod(nonPublic: true);
            if (getter is not null) {
                if (Delegate is Member member) {
                    foreach (var p in member.Value.ToPrimitives(context))
                        yield return p;
                }
                int argCount = getter.GetParameters().Length + (clrProp.IsStatic ? 0 : 1);
                yield return new Poly.Syntax.Primitives.CallExternal(getter, argCount, clrProp.IsStatic);
                yield break;
            }
        }

        // Lambda: inline body with arguments stored to parameter slots
        if (Delegate is Lambda lambda) {
            var env = context.GetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null);
            if (env is null) {
                env = new Poly.Syntax.Primitives.ExpansionEnvironment();
                context.SetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null, env);
            }

            // Map lambda parameters to slots
            foreach (var param in lambda.Parameters) {
                env.GetOrAssignSlot(param);
            }

            // Emit arguments and store each into its parameter slot.
            // StoreLocal re-pushes the value (StackEffect = (1,1)),
            // so we discard it — the body's Parameter primitives
            // will load from the slots directly.
            for (int i = 0; i < Arguments.Length; i++) {
                foreach (var p in Arguments[i].ToPrimitives(context))
                    yield return p;
                var paramNode = lambda.Parameters[i];
                if (env.TryGetSlot(paramNode, out var paramSlot)) {
                    yield return new Poly.Syntax.Primitives.StoreLocal(paramSlot);
                    yield return new Poly.Syntax.Primitives.Discard();
                }
            }

            // Emit body
            foreach (var p in lambda.Body.ToPrimitives(context)) yield return p;
            yield break;
        }

        // Generic fallback: emit delegate, args, then Call
        foreach (var p in Delegate.ToPrimitives(context)) yield return p;
        foreach (var arg in Arguments)
            foreach (var p in arg.ToPrimitives(context)) yield return p;
        yield return new Poly.Syntax.Primitives.Call(Arguments.Length, 0);
    }
}