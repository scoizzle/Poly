namespace Poly;

public static class ArrayExtensions {    

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(this T[] This, int index)
        => This != null
        && index >= 0 
        && index < This.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(this T[] This, int index, int lastIndex)
        => This != null
        && index >= 0
        && index < lastIndex
        && lastIndex <= This.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(this T[] This, int index, int lastIndex, int length)
        => This != null
        && index >= 0 
        && index + length <= lastIndex 
        && lastIndex <= This.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(this T[] This, int index, int lastIndex, T[] other, int otherIndex, int length)
        => This != null
        && index >= 0 
        && index + length <= lastIndex
        && lastIndex <= This.Length
        && other != null
        && otherIndex >= 0
        && otherIndex + length <= other.Length;        

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BoundsCheck<T>(
        this T?[] array,
            int min,
            int max,
            int index,
            int length)
    {
        return array is not null
            && min >= 0
            && max <= array.Length
            && index >= min
            && length >= 0
            && index + length < max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGet<T>(
        this T?[] array,
            int index,
            out T? value)
    {
        if (BoundsCheck(array, index)) {
            value = array[index];
            return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGet<T>(
        this T?[] array,
            int min,
            int max,
            int index,
            out T? value)
    {
        if (BoundsCheck(array, min, max, index)) {
            value = array[index];
            return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySet<T>(
        this T?[] array,
            int index,
            T? value)
    {
        if (BoundsCheck(array, index)) {
            array[index] = value;
            return true;
        }
        
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySet<T>(
        this T?[] array,
            int min,
            int max,
            int index,
            T? value)
    {
        if (BoundsCheck(array, min, max, index)) {
            array[index] = value;
            return true;
        }
        
        return false;
    }
}