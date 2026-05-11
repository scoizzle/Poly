using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed partial record InvokeAction {
    internal sealed record BindParameterFromCommand(InvokeAction Effect, string TargetParamName, Effect SourceEffect, string SourceOutputName) : DomainMutationCommand {
        public override void Apply() {
            ArgumentNullException.ThrowIfNull(TargetParamName);
            ArgumentNullException.ThrowIfNull(SourceEffect);
            ArgumentNullException.ThrowIfNull(SourceOutputName);

            if (Effect.TargetAction is null) {
                throw new InvalidOperationException($"Cannot bind parameter '{TargetParamName}': TargetAction is not set.");
            }

            if (!ReferenceEquals(Effect.Domain, SourceEffect.Domain)) {
                throw new InvalidOperationException("InvokeAction parameter bindings must stay within the same domain.");
            }

            if (!SourceEffect.Result.HasOutput(SourceOutputName)) {
                throw new InvalidOperationException(
                    $"Source effect '{SourceEffect.GetType().Name}' does not produce output '{SourceOutputName}'.");
            }

            var targetParam = Effect.TargetAction.Parameters.OfType<Property>()
                .FirstOrDefault(p => string.Equals(p.Name, TargetParamName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Parameter '{TargetParamName}' does not exist on action '{Effect.TargetAction.Name}'.");

            var sourceOutputType = SourceEffect.Result.Outputs[SourceOutputName];
            if (!DomainTypeAssignability.CanAssign(targetParam.Type, sourceOutputType)) {
                throw new InvalidOperationException(
                    $"Binding for parameter '{TargetParamName}' requires type '{targetParam.Type.Name}' but source output '{SourceOutputName}' has type '{sourceOutputType.Name}'.");
            }

            if (!Effect._parameterBindings.TryAdd(TargetParamName, new EffectValueRef(SourceEffect.GetType().Name, SourceOutputName))) {
                throw new InvalidOperationException(
                    $"Binding for parameter '{TargetParamName}' already exists on action '{Effect.TargetAction.Name}'.");
            }
        }

        public override void Rollback() => Effect._parameterBindings.Remove(TargetParamName);

        public override IEnumerable<Node> AffectedNodes => [Effect, SourceEffect];
    }
}