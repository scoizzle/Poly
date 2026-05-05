using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record AddTypeCommand(Domain Target, DomainType Type) : DomainMutationCommand {
        public override void Apply() {
            Target._objects.Add(Type);
        }
        public override void Rollback() {
            Target._objects.Remove(Type);
        }
        public override IEnumerable<Node> AffectedNodes {
            get {
                yield return Target;
                yield return Type;
                if (Type is Entity { ParentEntity: { } parent }) yield return parent;
            }
        }
    }
}