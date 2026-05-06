using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling.TypeSystem;

public abstract partial record DomainType {
    internal sealed record AddConstraintCommand(DomainType Type, Constraint Constraint) : DomainMutationCommand {
        public override void Apply() => Type._constraints.Add(Constraint);
        public override void Rollback() => Type._constraints.Remove(Constraint);
        public override IEnumerable<Node> AffectedNodes => [Type];
    }
}