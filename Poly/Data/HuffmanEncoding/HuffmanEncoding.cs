using System;
using System.Collections.Generic;

namespace Poly.Data {
    public abstract partial class HuffmanEncoding<TPriority, TValue> 
            where TPriority : IComparable<TPriority> 
            where TValue : IComparable<TValue> 
    {
        readonly Tree tree;

        public HuffmanEncoding(IEnumerable<TValue> dataset)
            => tree = BuildTree(CountFrequencies(dataset));

        public IEnumerable<bool> Encode(TValue value) =>
            tree.Encode(value);

        public IEnumerable<bool> Encode(IEnumerable<TValue> values) => 
            tree.Encode(values);

        public IEnumerable<TValue> Decode(IEnumerable<bool> encoded) =>
            tree.Decode(encoded);

        protected abstract TPriority Add(TPriority left, TPriority right);
        protected abstract TPriority Increment(TPriority left);
    }

    public class HuffmanEncoding<TValue> : HuffmanEncoding<long, TValue>
        where TValue : IComparable<TValue> 
    {
        public HuffmanEncoding(IEnumerable<TValue> dataset) : base(dataset) { }

        protected override long Add(long left, long right) => left + right;

        protected override long Increment(long left) => left + 1;
    }
}