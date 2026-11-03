using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Module method bodies hold name-only <see cref="Parameter"/> nodes. Binding swaps in the
/// method's typed parameters so analysis resolves them as real VM slots after
/// <c>SetArgs(this, …)</c>. Shared by every host that executes a module method.
/// </summary>
internal static class ModuleMethodBinding {

    internal static (Node Tree, IReadOnlyList<Parameter> RootParameters) BindTypedParameters(
        string entityName,
        MethodDefinitionNode method) {
        var body = method.Body
            ?? throw new InvalidOperationException(
                $"Module method '{method.Name}' on '{entityName}' has no body.");
        // C1a: action parameters are real VM slots after SetArgs(this, …).
        // Body Parameter nodes are name-only; swap in the method's typed Parameters
        // so analysis resolves them. Still Parameter nodes — not Member(this, name).
        var rootParameters = method.Parameters ?? [];
        if (rootParameters.Count == 0)
            return (body, rootParameters);
        var typed = rootParameters.ToDictionary(p => p.Name, StringComparer.Ordinal);
        Node BindTyped(Node node) {
            Node Recurse(Node n) => BindTyped(n);
            return node switch {
                Block b => new Block(
                    b.Nodes.Select(Recurse),
                    b.Variables.Select(Recurse)),
                IfStatement i => new IfStatement(
                    Recurse(i.Condition),
                    Recurse(i.ThenBranch),
                    i.ElseBranch is null ? null : Recurse(i.ElseBranch)),
                Return r => r.Value is null ? r : new Return(Recurse(r.Value)),
                Assignment a => new Assignment(Recurse(a.Destination), Recurse(a.Value)),
                Invoke inv => new Invoke(
                    Recurse(inv.Delegate),
                    [.. inv.Arguments.Select(Recurse)]) {
                    TypeArguments = inv.TypeArguments
                },
                Member m => new Member(Recurse(m.Value), m.MemberName),
                Poly.Ast.Nodes.Not n => new Poly.Ast.Nodes.Not(Recurse(n.Value)),
                Equal e => new Equal(Recurse(e.LeftHandValue), Recurse(e.RightHandValue)),
                NotEqual ne => new NotEqual(Recurse(ne.LeftHandValue), Recurse(ne.RightHandValue)),
                LessThan lt => new LessThan(Recurse(lt.LeftHandValue), Recurse(lt.RightHandValue)),
                LessThanOrEqual le => new LessThanOrEqual(Recurse(le.LeftHandValue), Recurse(le.RightHandValue)),
                GreaterThan gt => new GreaterThan(Recurse(gt.LeftHandValue), Recurse(gt.RightHandValue)),
                GreaterThanOrEqual ge => new GreaterThanOrEqual(Recurse(ge.LeftHandValue), Recurse(ge.RightHandValue)),
                Poly.Ast.Nodes.Add add => new Poly.Ast.Nodes.Add(Recurse(add.LeftHandValue), Recurse(add.RightHandValue)),
                Poly.Ast.Nodes.Subtract sub => new Poly.Ast.Nodes.Subtract(Recurse(sub.LeftHandValue), Recurse(sub.RightHandValue)),
                Poly.Ast.Nodes.Multiply mul => new Poly.Ast.Nodes.Multiply(Recurse(mul.LeftHandValue), Recurse(mul.RightHandValue)),
                Poly.Ast.Nodes.Divide div => new Poly.Ast.Nodes.Divide(Recurse(div.LeftHandValue), Recurse(div.RightHandValue)),
                Poly.Ast.Nodes.And and => new Poly.Ast.Nodes.And(Recurse(and.LeftHandValue), Recurse(and.RightHandValue)),
                Poly.Ast.Nodes.Or or => new Poly.Ast.Nodes.Or(Recurse(or.LeftHandValue), Recurse(or.RightHandValue)),
                Coalesce c => new Coalesce(Recurse(c.LeftHandValue), Recurse(c.RightHandValue)),
                TypeCast tc => new TypeCast(
                    Recurse(tc.Operand), Recurse(tc.TargetTypeReference), tc.IsChecked),
                New n => new New(Recurse(n.Type), [.. n.Arguments.Select(Recurse)]),
                ThrowStatement ts => new ThrowStatement(Recurse(ts.Exception)),
                TryCatchFinally t => new TryCatchFinally(
                    Recurse(t.TryBlock),
                    t.CatchClauses?.Select(cc => cc with {
                        ExceptionType = cc.ExceptionType is null ? null : Recurse(cc.ExceptionType),
                        Body = Recurse(cc.Body)
                    }).ToList(),
                    t.FinallyBlock is null ? null : Recurse(t.FinallyBlock)),
                ForEachLoop f => new ForEachLoop(
                    f.LoopVariable, Recurse(f.Collection), Recurse(f.Body), f.Label,
                    f.DeclaredType is null ? null : Recurse(f.DeclaredType)),
                ContinueStatement or BreakStatement => node,
                LabelDeclaration ld => new LabelDeclaration(ld.Name, Recurse(ld.Statement)),
                Conditional cond => new Conditional(
                    Recurse(cond.Condition), Recurse(cond.IfTrue), Recurse(cond.IfFalse)),
                UnaryMinus um => new UnaryMinus(Recurse(um.Operand)),
                NullForgiving nf => new NullForgiving(Recurse(nf.Operand)),
                Parameter p when typed.TryGetValue(p.Name, out var bound) => bound,
                ThisReference or Parameter or Variable or Constant
                    or NamedTypeReference or TypeReference
                    or PrimitiveTypeReference or ClrTypeReference
                    or OptionalTypeReference or CollectionTypeReference or MapTypeReference => node,
                _ => throw new InvalidOperationException(
                    $"Cannot bind typed parameters on {node.GetType().Name}.")
            };
        }
        return (BindTyped(body), rootParameters);
    }
}