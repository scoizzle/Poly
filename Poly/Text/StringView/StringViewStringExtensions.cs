namespace Poly;

public static class StringSliceStringExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringView AsSlice(this string str) => new(str);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringView AsSlice(this string str, int begin) => new(str, begin);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringView AsSlice(this string str, int begin, int length) => new(str, begin, length);
}