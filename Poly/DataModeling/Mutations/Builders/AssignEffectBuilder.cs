namespace Poly.DataModeling.Mutations.Builders;

using Poly.DataModeling.Mutations;
using Poly.DataModeling.Mutations.Effects;

public sealed class AssignEffectBuilder {
    private readonly EffectBuilder _parentBuilder;
    private readonly DataPropertyPath _propertyPath;

    internal AssignEffectBuilder(EffectBuilder parentBuilder, DataPropertyPath propertyPath) {
        _parentBuilder = parentBuilder;
        _propertyPath = propertyPath;
    }

    public EffectBuilder Constant(object? value) {
        _parentBuilder.AddEffect(new AssignEffect(_propertyPath, new ConstantValue(value)));
        return _parentBuilder;
    }

    public EffectBuilder Parameter(DataPropertyPath sourcePath) {
        ArgumentNullException.ThrowIfNull(sourcePath);
        _parentBuilder.AddEffect(new AssignEffect(_propertyPath, new ParameterValue(sourcePath)));
        return _parentBuilder;
    }
    
    public EffectBuilder Property(DataPropertyPath sourcePath) {
        ArgumentNullException.ThrowIfNull(sourcePath);
        _parentBuilder.AddEffect(new AssignEffect(_propertyPath, new PropertyValue(sourcePath)));
        return _parentBuilder;
    }
}