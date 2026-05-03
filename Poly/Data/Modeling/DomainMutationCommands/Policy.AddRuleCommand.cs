namespace Poly.Data.Modeling;

public sealed partial record Policy {
    internal sealed record AddRuleCommand(Policy Policy, Rule Rule) : DomainMutationCommand {
        public override void Apply() => Policy._rules.Add(Rule);
        public override void Rollback() => Policy._rules.Remove(Rule);
        public override IEnumerable<Node> AffectedNodes => [Policy];
    }
}