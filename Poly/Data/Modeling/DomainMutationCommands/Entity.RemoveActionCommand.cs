namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemoveActionCommand(Entity Entity, Action Action) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Entity._actions, Action);
        public override void Rollback() => DomainMutationCollection.Restore(Entity._actions, Action, _index);
        public override IEnumerable<Node> AffectedNodes => [Entity, Action];
    }

}