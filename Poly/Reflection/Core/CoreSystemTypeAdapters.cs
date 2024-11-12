namespace Poly.Reflection;

internal sealed class Int8Adapter : SpanableValueTypeAdapter<sbyte>;
internal sealed class Int16Adapter : SpanableValueTypeAdapter<short>;
internal sealed class Int32Adapter : SpanableValueTypeAdapter<int>;
internal sealed class Int64Adapter : SpanableValueTypeAdapter<long>;
internal sealed class UInt8Adapter : SpanableValueTypeAdapter<byte>;
internal sealed class UInt16Adapter : SpanableValueTypeAdapter<ushort>;
internal sealed class UInt32Adapter : SpanableValueTypeAdapter<uint>;
internal sealed class UInt64Adapter : SpanableValueTypeAdapter<ulong>;
internal sealed class SingleAdapter : SpanableValueTypeAdapter<float>;
internal sealed class DoubleAdapter : SpanableValueTypeAdapter<double>;
internal sealed class DecimalAdapter : SpanableValueTypeAdapter<decimal>;
internal sealed class GuidAdapter : SpanableValueTypeAdapter<Guid>;
