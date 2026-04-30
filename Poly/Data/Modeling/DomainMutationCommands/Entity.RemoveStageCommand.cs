namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemoveStageCommand(Entity Entity, Stage Stage) : DomainMutationCommand {
        public override void Apply() => Entity._stages.Remove(Stage);
        public override void Rollback() {
            Stage.AttachToEntity(Entity);
            Entity._stages.Add(Stage);
        }
        public override IEnumerable<Node> AffectedNodes => [Entity, Stage];
    }

}