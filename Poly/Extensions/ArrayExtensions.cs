namespace Poly.Extensions;

public static partial class ArrayExtensions {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(this ReadOnlySpan<T> span, int index)
        => index >= 0
        && index < span.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(this ReadOnlySpan<T> span, int index, int lastIndex)
        => index >= 0
        && index < lastIndex
        && lastIndex <= span.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(this ReadOnlySpan<T> span, int index, int lastIndex, int length)
        => index >= 0
        && index + length <= lastIndex
        && lastIndex <= span.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(this ReadOnlySpan<T> span, int index, int lastIndex, ReadOnlySpan<T> other, int otherIndex, int length)
        => index >= 0
        && index + length <= lastIndex
        && lastIndex <= span.Length
        && otherIndex >= 0
        && otherIndex + length <= other.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(
        this ReadOnlySpan<T> array,
            int min,
            int max,
            int index,
            int length) {
        return min >= 0
            && max <= array.Length
            && index >= min
            && length >= 0
            && index + length < max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGet<T>(
        this ReadOnlySpan<T> array,
            int index,
            out T? value) {
        if (array.BoundsCheck(index)) {
            value = array[index];
            return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGet<T>(
        this ReadOnlySpan<T> array,
            int min,
            int max,
            int index,
            out T? value) {
        if (array.BoundsCheck(min, max, index)) {
            value = array[index];
            return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySet<T>(
        this Span<T> array,
            int index,
            T value) {
        if (BoundsCheck<T>(array, index)) {
            array[index] = value;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySet<T>(
        this Span<T> array,
            int min,
            int max,
            int index,
            T value) {
        if (BoundsCheck<T>(array, min, max, index)) {
            array[index] = value;
            return true;
        }

        return false;
    }
}