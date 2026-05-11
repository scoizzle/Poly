using Poly.Data.Modeling.Effects;

namespace Poly.Data.Modeling.Effects;

public abstract partial record Effect {
    internal sealed record BindOutputToCommand(Effect SourceEffect, string OutputName, Effect TargetEffect, string TargetParamName) : DomainMutationCommand {
        private EffectValueRef? _previousBinding;

        public override void Apply() {
            if (!ReferenceEquals(SourceEffect.Domain, TargetEffect.Domain)) {
                throw new InvalidOperationException("Cross-effect bindings must stay within the same domain.");
            }

            if (!SourceEffect.Result.HasOutput(OutputName)) {
                throw new InvalidOperationException(
                    $"Effect '{SourceEffect.GetType().Name}' does not produce output '{OutputName}'.");
            }

            _previousBinding = TargetEffect.SetIncomingBinding(TargetParamName, new EffectValueRef(SourceEffect.GetType().Name, OutputName));
        }

        public override void Rollback() => TargetEffect.RestoreIncomingBinding(TargetParamName, _previousBinding);

        public override IEnumerable<Node> AffectedNodes => [SourceEffect, TargetEffect];
    }
}