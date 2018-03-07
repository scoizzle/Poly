using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Data {

    public partial class ManagedArray<T> : IList<T>, IEnumerable<T> {
        public T[] Elements;

        public int Count { get; private set; }

        public bool IsReadOnly => false;

        public T this[int index] {
            get => (index >= 0 && index < Count) ? Elements[index] : default;
            set {
                if (index >= 0 && index < Count)
                    Elements[index] = value;
            }
        }

        public ManagedArray() : this(4) {
        }

        public ManagedArray(int baseLength) {
            Elements = new T[baseLength];
        }

        public ManagedArray(T[] baseArray) {
            Elements = baseArray;
            Count = Elements.Length;
        }

        public ManagedArray(ManagedArray<T> Base) {
            Elements = new T[Base.Count == 0 ? 4 : Base.Count];
            Base.CopyTo(this);
        }

        public bool Contains(T item) {
            return IndexOf(item) >= 0;
        }

        public void CopyTo(ManagedArray<T> destination) {
            destination.ValidateSizeForInsert(Count + destination.Count);

            Array.Copy(Elements, 0, destination.Elements, destination.Count, Count);
            destination.Count += Count;
        }

        public void CopyTo(T[] array, int arrayIndex) {
            if (array == null || arrayIndex < 0 || arrayIndex >= array.Length || arrayIndex + Count >= array.Length)
                return;

            Array.Copy(Elements, 0, array, arrayIndex, Count);
        }

        public void Add(T value) {
            ValidateSizeForInsert();
            Elements[Count] = value;
            Count++;
        }

        public void Add(T[] value) {
            var length = value.Length;
            ValidateSizeForInsert(Count + length);

            Array.Copy(value, 0, Elements, Count, length);
            Count += length;
        }

        public void Insert(int index, T value) {
            if (index < 0)
                return;

            if (index > Elements.Length) {
                ValidateSizeForInsert(index + 1);
                Elements[index] = value;
            }
            else {
                ValidateSizeForInsert();
                Array.Copy(Elements, index, Elements, index + 1, Count - index);
                Elements[index] = value;
                Count++;
            }
        }

        public bool Remove(T value) {
            return TryRemoveAt(IndexOf(value));
        }

        public void RemoveAt(int index) {
            TryRemoveAt(index);
        }

        public void RemoveAt(params int[] indicies) =>
            RemoveAt(indicies, 0, indicies.Length);
        
        public void RemoveAt(int[] indicies, int index, int count) {
            if (index < 0 || index + count > indicies.Length)
                return;

            while (index < count)
                Elements[indicies[index++]] = default;

            for (int i = 0; i < Elements.Length - 2; i++) {
                if (Equals(Elements[i], default(T)))
                    Elements[i] = Elements[i + 1];
            }

            Count -= count;
        }


        public bool TryRemoveAt(int index) {
            if (index < 0 || index >= Count)
                return false;

            if (index == Count - 1) {
                Elements[index] = default;
                Count--;
            }
            else {
                var End = Count;
                var Post = End - index - 1;

                Array.Copy(Elements, index + 1, Elements, index, Post);

                Elements[End] = default;
                Count--;
            }

            return true;
        }

        public bool TryRemoveAt(int index, int count) {
            if (index < 0 || count <= 0 || index + count > Count)
                return false;

            var to_move = Count - count - index;
            var last_index = index + count;

            Array.Copy(Elements, last_index, Elements, index, to_move);
            Array.Clear(Elements, index + to_move, Count - count);

            Count -= count;
            return true;
        }

        public void RemoveWhere(Func<T, bool> selector) {
            for (var i = 0; i < Count; i++) {
                if (selector(Elements[i]))
                    TryRemoveAt(i);
            }
        }

        public void Clear() {
            Array.Clear(Elements, 0, Count);
            Count = 0;
        }

        public int IndexOf(T value) {
            for (var i = 0; i < Count; i++) {
                if (Elements[i].Equals(value))
                    return i;
            }
            return -1;
        }

        public void Constrain() {
            if (Count < Elements.Length)
                Array.Resize(ref Elements, Count);
        }

        private void ValidateSizeForInsert() {
            if (Elements.Length <= Count + 1)
                Array.Resize(ref Elements, Elements.Length * 2);
        }

        private void ValidateSizeForInsert(int newLen) {
            if (Elements.Length <= newLen)
                Array.Resize(ref Elements, newLen);
        }

        public IEnumerator<T> GetEnumerator() {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return (IEnumerator)new Enumerator(this);
        }

        public struct Enumerator : IEnumerator<T> {
            private int index, len;
            private T[] list;

            public T Current { get => list[index]; }
            object IEnumerator.Current { get { return Current; } }

            internal Enumerator(ManagedArray<T> array) {
                index = -1;
                len = array.Count;

                list = array.Elements;
            }

            public void Dispose() {
            }

            public bool MoveNext() {
                return ++index < len;
            }

            void IEnumerator.Reset() {
                index = -1;
            }
        }
    }
}