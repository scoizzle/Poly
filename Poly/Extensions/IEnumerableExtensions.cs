namespace Poly;

public static class IEnumerableExtensions
{
    public static void ForEach<T>(this IEnumerable<T> set, Action<T> action)
    {
        foreach (var value in set)
            action(value);
    }
}