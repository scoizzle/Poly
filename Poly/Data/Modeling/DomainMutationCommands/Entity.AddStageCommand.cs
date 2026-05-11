namespace Poly.Data.Modeling;

public partial record Entity {
    internal sealed record AddStageCommand(Entity Entity, Stage Stage) : DomainMutationCommand {
        public override void Apply() {
            Stage.AttachToEntity(Entity);
            Entity._stages.Add(Stage);
        }
        public override void Rollback() {
            _ = DomainMutationCollection.RemoveAt(Entity._stages, Stage);
            Stage.DetachFromEntity(Entity);
        }
        public override IEnumerable<Node> AffectedNodes {
            get {
                yield return Entity;
                yield return Stage;
                if (Entity.ParentEntity is { } parent) yield return parent;
            }
        }
    }

}