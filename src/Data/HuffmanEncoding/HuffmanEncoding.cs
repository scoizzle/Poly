using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Poly.Data {
    public partial class HuffmanEncoding<TPriority, TValue> where TValue : IComparable<TValue>{
        Tree Trunk;

        public HuffmanEncoding(Tree tree) {
            Trunk = tree;
        }

        public HuffmanEncoding(TValue[] values) 
             : this(new Tree(new FrequencyCounter(values).Counts)) {
        }

        public HuffmanEncoding(IEnumerable<(IEnumerable<bool> Path, TValue Value)> leafs) 
            : this (new Tree(leafs)) {

        }

        static Func<TPriority, TPriority, TPriority> AddPriority = MakeAdd();
        static Func<TPriority, TPriority> IncPriority = MakeIncrement();

        static Func<TPriority, TPriority, TPriority> MakeAdd() {
            var left = Expression.Parameter(typeof(TPriority), "left");
            var right = Expression.Parameter(typeof(TPriority), "right");

            return Expression.Lambda<Func<TPriority, TPriority, TPriority>>(
                BinaryExpression.Add(left, right),
                left,
                right
            ).Compile();
        }

        static Func<TPriority, TPriority> MakeIncrement() {
            var left = Expression.Parameter(typeof(TPriority), "left");
            var right = Expression.Constant(1);

            return Expression.Lambda<Func<TPriority, TPriority>>(
                BinaryExpression.Add(left, right),
                left
            ).Compile();
        }
    }
}