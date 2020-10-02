using System;
using System.Collections;
using System.Collections.Generic;

namespace Poly {
    public struct ArrayIterator<T> : IEnumerator<T> {
        public ArrayIterator(T[] array) {
            Array = array ?? throw new ArgumentNullException(nameof(array));
            Index = default;
        }        

        public bool IsSynchronized => false;

        public object SyncRoot => new object();

        private T this[int index] =>
            index >= 0 && index < Array.Length ?
                Array[index] :
                default;

        public int Count
            => Array.Length;

        public T[] Array { get; }

        public int Index { get; private set; }

        public bool IsDone
            => Index >= Count;

        public bool IsFirst
            => Index == 0;

        public bool IsLast
            => Index == Count - 1;

        public T Previous
            => this[Index - 1];

        public T Current
            => this[Index];

        public T Next
            => this[Index + 1];

        public T First
            => this[0];

        public T Last
            => this[Count - 1];

        object IEnumerator.Current => this.Current;

        public bool MoveNext()
            => ++Index < Count;

        public void Reset()
            => Index = default;

        public void Dispose()
            => Index = default;
    }
}
