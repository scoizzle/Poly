namespace Poly.Data;

public static class HuffmanEncoding<TPriority, TValue>
    where TPriority : INumber<TPriority>, 
    IAdditiveIdentity<TPriority, TPriority>, 
    IBitwiseOperators<TPriority, TPriority, TPriority>,
    IShiftOperators<TPriority, TPriority, TPriority>
    where TValue : IComparable<TValue>
{
    public HuffmanEncoding(IEnumerable<TValue> values)
    {
        Tree = new(values);
        Encoder = new(Tree);
    }

    public HuffmanEncodingTree<TPriority, TValue> Tree { get; init; }
    public HuffmanEncoder<TPriority, TValue> Encoder { get; init; }

    public IEnumerable<bool> Encode(TValue value) => Encoder.Encode(value);
    public IEnumerable<bool> EncodeSet(IEnumerable<TValue> values) => values.SelectMany(e => Tree.GetNodePath(e)?.Path!);
    public TValue? Decode(IEnumerable<bool> path) => Tree.GetValueFromPath(path);
    public IEnumerable<TValue?> DecodeSet(IEnumerable<bool> path) => Tree.GetValuesFromPaths(path);
}