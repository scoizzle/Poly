namespace Poly.Text.Matching
{
    public delegate bool TryCompareDelegate(StringView view);
    public delegate bool TryExtractDelegate(StringView view, object obj);
    public delegate bool TrySerializeDelegate<T>(StringBuilder view, T value);
    public delegate bool TryDeserializeDelegate<T>(StringView view, out T value);

    public static class Evaluation
    {
        public static readonly TryCompareDelegate DefaultComparisonTrue = (StringView view) => true;
        public static readonly TryCompareDelegate DefaultComparisonFalse = (StringView view) => false;

        public static readonly TryExtractDelegate DefaultExtractionTrue = (StringView view, object obj) => true;
        public static readonly TryExtractDelegate DefaultExtractionFalse = (StringView view, object obj) => false;
    }
}