namespace Poly.DataModeling.Mutations.Builders;

using Poly.DataModeling.Mutations;
using Poly.DataModeling.Mutations.Effects;

public sealed class EffectBuilder {
    private readonly List<Effect> _effects = new();

    public AssignEffectBuilder Assign(DataPropertyPath propertyPath) {
        ArgumentNullException.ThrowIfNull(propertyPath);
        return new AssignEffectBuilder(this, propertyPath);
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
