namespace Poly;

public static class ArrayExtensions 
{
    public static bool BoundsCheck<T>(
        this T[] array,
            int index)
    {
        return array is not null
            && index >= 0
            && index < array.Length;
    }

    public static bool BoundsCheck<T>(
        this T[] array,
                int min,
                int max,
                int index)
    {
        return array is not null
            && min >= 0
            && max <= array.Length
            && index >= min
            && index < max;
    }

    public static bool BoundsCheck<T>(
        this T[] array,
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

    public static bool TryGet<T>(
        this T[] array,
            int index,
            out T value)
    {
        if (BoundsCheck(array, index)) {
            value = array[index];
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryGet<T>(
        this T[] array,
            int min,
            int max,
            int index,
            out T value)
    {
        if (BoundsCheck(array, min, max, index)) {
            value = array[index];
            return true;
        }

        value = default;
        return false;
    }

    public static bool TrySet<T>(
        this T[] array,
            int index,
            T value)
    {
        if (BoundsCheck(array, index)) {
            array[index] = value;
            return true;
        }
        
        return false;
    }

    public static bool TrySet<T>(
        this T[] array,
            int min,
            int max,
            int index,
            T value)
    {
        if (BoundsCheck(array, min, max, index)) {
            array[index] = value;
            return true;
        }
        
        return false;
    }
}