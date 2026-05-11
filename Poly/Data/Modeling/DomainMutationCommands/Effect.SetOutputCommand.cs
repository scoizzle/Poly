using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public abstract partial record Effect {
    internal sealed record SetOutputCommand(Effect Effect, string OutputName, DomainType Type) : DomainMutationCommand {
        private DomainType? _previousType;

        public override void Apply() {
            ArgumentNullException.ThrowIfNull(Type);

            if (!ReferenceEquals(Effect.Domain, Type.Domain)) {
                throw new InvalidOperationException("Effect outputs must use types from the same domain.");
            }

            _previousType = Effect.Result.SetOutput(OutputName, Type);
        }

        public override void Rollback() => Effect.Result.RestoreOutput(OutputName, _previousType);

        public override IEnumerable<Node> AffectedNodes => [Effect, Type];
    }
}