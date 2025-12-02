namespace Poly.DataModeling.Builders;

using Poly.DataModeling.Mutations;
using Poly.DataModeling.Mutations.Effects;

public sealed class EffectBuilder {
    private readonly List<Effect> _effects = new();

    public AssignEffectBuilder Assign(DataPropertyPath propertyPath) {
        ArgumentNullException.ThrowIfNull(propertyPath);
        return new AssignEffectBuilder(this, propertyName);
    }

    public EffectBuilder SetConst(string propertyName, object? value) {
        ArgumentNullException.ThrowIfNull(propertyName);
        _effects.Add(new SetEffect(propertyName, new ConstantValue(value)));
        return this;
    }

    public EffectBuilder SetFromParam(string propertyName, string parameterName) {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(parameterName);
        _effects.Add(new SetEffect(propertyName, new ParameterValue(parameterName)));
        return this;
    }

    public EffectBuilder IncrementConst(string propertyName, double amount) {
        ArgumentNullException.ThrowIfNull(propertyName);
        _effects.Add(new IncrementEffect(propertyName, new ConstantValue(amount)));
        return this;
    }

    public EffectBuilder IncrementFromParam(string propertyName, string parameterName) {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(parameterName);
        _effects.Add(new IncrementEffect(propertyName, new ParameterValue(parameterName)));
        return this;
    }

    public EffectBuilder AddToFromParam(string propertyName, string parameterName) {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(parameterName);
        _effects.Add(new AddToEffect(propertyName, new ParameterValue(parameterName)));
        return this;
    }

    public EffectBuilder RemoveFromFromParam(string propertyName, string parameterName) {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(parameterName);
        _effects.Add(new RemoveFromEffect(propertyName, new ParameterValue(parameterName)));
        return this;
    }

    internal EffectBuilder AddEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
        return this;
    }

    internal IEnumerable<Effect> Build() => _effects;
}
