namespace Poly.Data.Modeling;

public sealed partial record Stage {
    internal sealed record RemoveActionCommand(Stage Stage, Action Action) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Stage._actions, Action);
        public override void Rollback() => DomainMutationCollection.Restore(Stage._actions, Action, _index);
        public override IEnumerable<Node> AffectedNodes => [Stage, Action];
    }
}