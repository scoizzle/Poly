namespace Poly.Data.Modeling;

public sealed partial record Relationship {
    internal sealed record SetShapeCommand(
        Relationship Relationship,
        Entity Source,
        Entity Target,
        RelationshipCardinality Cardinality,
        bool SourceOwnsTarget) : DomainMutationCommand {
        private readonly Entity _prevSource = Relationship.Source;
        private readonly Entity _prevTarget = Relationship.Target;
        private readonly RelationshipCardinality _prevCardinality = Relationship.Cardinality;
        private readonly bool _prevSourceOwnsTarget = Relationship.SourceOwnsTarget;

        public override void Apply() {
            Relationship.Source = Source;
            Relationship.Target = Target;
            Relationship.Cardinality = Cardinality;
            Relationship.SourceOwnsTarget = SourceOwnsTarget;
        }

        public override void Rollback() {
            Relationship.Source = _prevSource;
            Relationship.Target = _prevTarget;
            Relationship.Cardinality = _prevCardinality;
            Relationship.SourceOwnsTarget = _prevSourceOwnsTarget;
        }

        public override IEnumerable<Node> AffectedNodes => [Relationship, _prevSource, _prevTarget, Source, Target];
    }
}