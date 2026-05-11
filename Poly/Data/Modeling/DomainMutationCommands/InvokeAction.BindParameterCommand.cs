using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed partial record InvokeAction {
    internal sealed record BindParameterCommand(InvokeAction Effect, Property TargetParameter, DomainValue Value) : DomainMutationCommand {
        public override void Apply() {
            ArgumentNullException.ThrowIfNull(TargetParameter);
            ArgumentNullException.ThrowIfNull(Value);

            if (Effect.TargetAction is null) {
                throw new InvalidOperationException("Cannot bind a parameter before TargetAction is set.");
            }

            if (!ReferenceEquals(Effect.Domain, TargetParameter.Domain) || !ReferenceEquals(Effect.Domain, Value.Domain)) {
                throw new InvalidOperationException("InvokeAction parameter bindings must stay within the same domain.");
            }

            if (!Effect.TargetAction.Parameters.OfType<Property>().Any(p => string.Equals(p.Name, TargetParameter.Name, StringComparison.Ordinal))) {
                throw new InvalidOperationException(
                    $"Parameter '{TargetParameter.Name}' does not exist on action '{Effect.TargetAction.Name}'.");
            }

            if (!DomainTypeAssignability.CanAssign(TargetParameter.Type, Value.Type)) {
                throw new InvalidOperationException(
                    $"Binding for parameter '{TargetParameter.Name}' requires type '{TargetParameter.Type.Name}' but got '{Value.Type.Name}'.");
            }

            if (!Effect._parameterBindings.TryAdd(TargetParameter.Name, Value)) {
                throw new InvalidOperationException(
                    $"Binding for parameter '{TargetParameter.Name}' already exists on action '{Effect.TargetAction.Name}'.");
            }
        }

        public override void Rollback() => Effect._parameterBindings.Remove(TargetParameter.Name);

        public override IEnumerable<Node> AffectedNodes => [Effect, TargetParameter, Value];
    }
}