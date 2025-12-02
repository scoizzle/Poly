namespace Poly.DataModeling.Mutations.Builders;

using Poly.DataModeling.Mutations.Effects;

public sealed class AssignEffectBuilder {
    private readonly EffectBuilder _parentBuilder;
    private readonly DataPropertyPath _propertyPath;

    internal AssignEffectBuilder(EffectBuilder parentBuilder, DataPropertyPath propertyPath) {
        _parentBuilder = parentBuilder;
        _propertyPath = propertyPath;
    }

    public EffectBuilder FromConstant(object? value) {
        _parentBuilder.AddEffect(new SetEffect(_propertyPath, new ConstantValue(value)));
        return _parentBuilder;
    }

    public EffectBuilder FromParameter(DataPropertyPath sourcePath) {
        ArgumentNullException.ThrowIfNull(sourcePath);
        _parentBuilder.AddEffect(new SetEffect(_propertyPath, new ParameterValue(parameterName)));
        return _parentBuilder;
    }
}