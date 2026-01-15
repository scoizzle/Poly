namespace Poly;

public static class ZipAllExtension {

    extension<T1>(IEnumerable<T1> first) {
        /// <summary>
        /// Zips two enumerables together, yielding tuples of their values. If one enumerable is shorter
        /// than the other, the remaining values from the longer enumerable are paired with default values
        /// of the shorter enumerable's element type.
        /// </summary>
        /// <typeparam name="T1">The type of the elements in the first sequence.</typeparam>
        /// <typeparam name="T2">The type of the elements in the second sequence.</typeparam>
        /// <param name="first">The first sequence to zip.</param>
        /// <param name="second">The second sequence to zip.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="ValueTuple{T1, T2}"/> containing pairs of elements 
        /// from both sequences. When one sequence is exhausted, default values are used for the remaining elements 
        /// of the shorter sequence.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="first"/> or <paramref name="second"/> is <c>null</c>.</exception>
        /// <example>
        /// <code>
        /// var numbers = new[] { 1, 2, 3 };
        /// var letters = new[] { "a", "b" };
        /// var zipped = numbers.ZipAll(letters);
        /// // Result: (1, "a"), (2, "b"), (3, null)
        /// </code>
        /// </example>
        public IEnumerable<ValueTuple<T1?, T2?>> ZipAll<T2>(IEnumerable<T2> second)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);

            using var enum1 = first.GetEnumerator();
            using var enum2 = second.GetEnumerator();

            var firstHasNext = enum1.MoveNext();
            var secondHasNext = enum2.MoveNext();

            while (firstHasNext || secondHasNext) {
                var value1 = firstHasNext ? enum1.Current : default;
                var value2 = secondHasNext ? enum2.Current : default;

                yield return (value1, value2);

                firstHasNext = firstHasNext && enum1.MoveNext();
                secondHasNext = secondHasNext && enum2.MoveNext();
            }
        }
    }
}