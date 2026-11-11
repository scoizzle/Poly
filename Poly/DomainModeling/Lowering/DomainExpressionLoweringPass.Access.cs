using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Lowering;

// Access: property, parameter, literal and owned-member reads.

public sealed partial class DomainExpressionLoweringPass {
    protected override LoweredExpression PropertyAccess(PropertyAccess p) {
        // Session ident folds (clocks) and Guid through LowerDefaultExpression.
        // Bare fragments without those tables stay Member so analysis can fail
        // closed on unknown property.
        var runtime = EffectLoweringPass.LowerDefaultExpression(
            p, typeHint: null, EffectiveMeaning, EffectiveForms);
        if (runtime is not null) return LoweredExpression.Of(runtime);

        // Action parameters win over a property of the same name, including
        // inside a quantifier body (the nested pass inherits ActionParameterNames).
        if (_actionParameterNames?.Contains(p.Name) == true)
            return LoweredExpression.Of(new Parameter(p.Name));
        return LoweredExpression.Of(new Member(_currentSubject, ResolveName(p.Name)));
    }

    protected override LoweredExpression ParameterAccess(ParameterAccess p)
        => LoweredExpression.Of(
            _parameters.TryGetValue(p.Name, out var param) ? param : new Parameter(p.Name));

    protected override LoweredExpression Literal(Literal l)
        => LoweredExpression.Of(new Constant(l.Value));

    protected override LoweredExpression OwnedAccess(OwnedAccess oa)
        => Route(oa.Inner, new Member(_currentSubject, ResolveName(oa.OwnedName)));
}