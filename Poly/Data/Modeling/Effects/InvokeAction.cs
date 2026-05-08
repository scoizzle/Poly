using System.Linq;

using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record InvokeAction(Domain Domain) : Effect(Domain) {
    private readonly Dictionary<string, DomainValue> _parameterBindings = new(StringComparer.Ordinal);

    public required Action TargetAction { get; init; }

    public IReadOnlyDictionary<string, DomainValue> ParameterBindings => _parameterBindings;

    public void BindParameter(Property targetParameter, DomainValue value) {
        ArgumentNullException.ThrowIfNull(targetParameter);
        ArgumentNullException.ThrowIfNull(value);

        if (!TargetAction.Parameters.OfType<Property>().Any(p => string.Equals(p.Name, targetParameter.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException(
                $"Parameter '{targetParameter.Name}' does not exist on action '{TargetAction.Name}'.");
        }

        if (!ReferenceEquals(targetParameter.Type, value.Type)) {
            throw new InvalidOperationException(
                $"Binding for parameter '{targetParameter.Name}' requires type '{targetParameter.Type.Name}' but got '{value.Type.Name}'.");
        }

        if (!_parameterBindings.TryAdd(targetParameter.Name, value)) {
            throw new InvalidOperationException(
                $"Binding for parameter '{targetParameter.Name}' already exists on action '{TargetAction.Name}'.");
        }
    }

    internal bool HasBindingFor(Property targetParameter) => _parameterBindings.ContainsKey(targetParameter.Name);

    /// <summary>
    /// Bind a parameter to a specific named output from a prior effect.
    /// At code generation, this becomes: BindParameter(param, priorEffect.Output[name]).
    /// </summary>
    public void BindParameterFrom(string targetParamName, Effect sourceEffect, string sourceOutputName) {
        ArgumentNullException.ThrowIfNull(targetParamName);
        ArgumentNullException.ThrowIfNull(sourceEffect);
        ArgumentNullException.ThrowIfNull(sourceOutputName);

        if (TargetAction is null) {
            throw new InvalidOperationException(
                $"Cannot bind parameter '{targetParamName}': TargetAction is not set.");
        }

        if (!sourceEffect.Result.HasOutput(sourceOutputName)) {
            throw new InvalidOperationException(
                $"Source effect '{sourceEffect.GetType().Name}' does not produce output '{sourceOutputName}'.");
        }

        // Find the target parameter on TargetAction
        var targetParam = TargetAction.Parameters.OfType<Property>()
            .FirstOrDefault(p => string.Equals(p.Name, targetParamName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Parameter '{targetParamName}' does not exist on action '{TargetAction.Name}'.");

        // Store the wiring: parameter <- EffectValueRef
        if (!_parameterBindings.TryAdd(targetParamName, new EffectValueRef(sourceEffect.GetType().Name, sourceOutputName))) {
            throw new InvalidOperationException(
                $"Binding for parameter '{targetParamName}' already exists on action '{TargetAction.Name}'.");
        }
    }
}