namespace Poly.Introspection;

class TypeAdapterFeatureCollection : ITypeAdapterFeatureCollection
{
    private readonly Dictionary<Type, object> features = new();

    public object this[Type key]
    {
        get => features[key];
        set => features[key] = value;
    }

    public TFeature Get<TFeature>(Type key) => (TFeature)features[key];

    public void Set<TFeature>(TFeature value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        features[typeof(TFeature)] = value;
    }

    public static Lazy<ITypeAdapterFeatureCollection> NewLazyFactory() => new(() => new TypeAdapterFeatureCollection());
}