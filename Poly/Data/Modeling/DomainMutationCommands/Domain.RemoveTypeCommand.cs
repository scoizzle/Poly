using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed partial record Domain {
    internal sealed record RemoveTypeCommand(Domain Target, DomainType Type) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() {
            _index = DomainMutationCollection.RemoveAt(Target._objects, Type);
        }
        public override void Rollback() {
            DomainMutationCollection.Restore(Target._objects, Type, _index);
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