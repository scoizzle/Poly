namespace Poly.Data.Modeling;

public sealed partial record Stage {
    internal sealed record RemoveActionCommand(Stage Stage, Action Action) : DomainMutationCommand {
        public override void Apply() => Stage._actions.Remove(Action);
        public override void Rollback() => Stage._actions.Add(Action);
        public override IEnumerable<Node> AffectedNodes => [Stage, Action];
    }
}