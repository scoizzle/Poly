using System;
using System.Collections.Generic;

namespace Poly {
    public readonly struct StringViewEqualityComparer : IEqualityComparer<StringView>
    {
        public readonly StringComparison Comparison;

        public StringViewEqualityComparer(StringComparison comparison = StringComparison.Ordinal)
            => Comparison = comparison;

        public bool Equals(StringView x, StringView y)
            => x.Equals(y, Comparison);

        public int GetHashCode(StringView obj)
            => obj.GetHashCode();

        public static readonly StringViewEqualityComparer Ordinal = new StringViewEqualityComparer(StringComparison.Ordinal);

        public static readonly StringViewEqualityComparer OrdinalIgnoreCase = new StringViewEqualityComparer(StringComparison.OrdinalIgnoreCase);

        public static readonly StringViewEqualityComparer CurrentCulture = new StringViewEqualityComparer(StringComparison.CurrentCulture);

        public static readonly StringViewEqualityComparer CurrentCultureIgnoreCase = new StringViewEqualityComparer(StringComparison.CurrentCultureIgnoreCase);

        public static readonly StringViewEqualityComparer InvariantCulture = new StringViewEqualityComparer(StringComparison.InvariantCulture);

        public static readonly StringViewEqualityComparer InvariantCultureIgnoreCase = new StringViewEqualityComparer(StringComparison.InvariantCultureIgnoreCase);
    }
}