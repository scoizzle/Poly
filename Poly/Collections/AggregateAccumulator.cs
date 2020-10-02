using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Collections {
    public class AggregateAccumulator<T> : List<Func<T,T>> {
        public AggregateAccumulator(T defaultValue)
            => Default = defaultValue;

        public T Default { get; }

        public T Value => this.Aggregate(Default, (value, function) => function(value));
    }
}