namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemoveActionCommand(Entity Entity, Action Action) : DomainMutationCommand {
        public override void Apply() => Entity._actions.Remove(Action);
        public override void Rollback() => Entity._actions.Add(Action);
        public override IEnumerable<Node> AffectedNodes => [Entity, Action];
    }

}