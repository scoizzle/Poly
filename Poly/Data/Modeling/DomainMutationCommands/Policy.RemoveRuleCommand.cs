namespace Poly.Data.Modeling;

public sealed partial record Policy {
    internal sealed record RemoveRuleCommand(Policy Policy, Rule Rule) : DomainMutationCommand {
        private int _index = -1;

        public override void Apply() => _index = DomainMutationCollection.RemoveAt(Policy._rules, Rule);
        public override void Rollback() => DomainMutationCollection.Restore(Policy._rules, Rule, _index);
        public override IEnumerable<Node> AffectedNodes => [Policy];
    }
}