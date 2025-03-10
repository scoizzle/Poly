namespace Poly.Introspection;

public interface ITypeAdapterFeatureCollection
{
    public object this[Type key] { get; set; }
    public TFeature Get<TFeature>(Type key);
    public void Set<TFeature>(TFeature value);
}
