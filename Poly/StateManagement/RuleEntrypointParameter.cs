using Poly.Interpretation;

public sealed class RuleInterpretationContext : Context {
    public RuleInterpretationContext() : base() {
        EntryPoint = new Parameter("_");
    }
    internal Parameter EntryPoint { get; } = new Parameter("_");
}