namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record RemoveStageCommand(Entity Entity, Stage Stage) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() {
            _index = DomainMutationCollection.RemoveAt(Entity._stages, Stage);
            Stage.DetachFromEntity(Entity);
        }

        public override void Rollback() {
            Stage.AttachToEntity(Entity);
            DomainMutationCollection.Restore(Entity._stages, Stage, _index);
        }
        public override IEnumerable<Node> AffectedNodes => [Entity, Stage];
    }

}