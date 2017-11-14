using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public class ManagedArray<T> : IEnumerable<T> {
        public T[] Elements;

        public int Count { get; private set; }

        public T this[int index]
        {
            get
            {
                if (index >= 0 && index < Count)
                    return Elements[index];
                return default(T);
            }
            set
            {
                if (index >= 0 && index < Count)
                    Elements[index] = value;
            }
        }

        public ManagedArray() : this(4) { }

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

        public void CopyTo(ManagedArray<T> destination) {
            destination.ValidateSizeForInsert(Count + destination.Count);

            Array.Copy(Elements, 0, destination.Elements, destination.Count, Count);
            destination.Count += Count;
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

        public void Remove(T value) {
            RemoveAt(IndexOf(value));
        }

        public bool RemoveAt(int Index) {
            if (Index < 0 || Index >= Count)
                return false;

            if (Index == Count - 1) {
                Elements[Index] = default(T);
                Count--;
                return true;
            }

            var End = Count;
            var Post = End - Index - 1;
            
            Array.Copy(Elements, Index + 1, Elements, Index, Post);

            Elements[End] = default(T);
            Count--;
            return true;
        }

        public void RemoveAt(int Index, int Count) {
            if (Index < 0 || Count <= 0 || Index + Count > Count)
                return;

            var to_move = Count - Count;
            var chunk_end = Index + Count;

            Array.Copy(Elements, chunk_end, Elements, Index, to_move - Index);
            Array.Clear(Elements, Count, Elements.Length - Count);

            Count = to_move;
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
            int index, len;
            T[] list;

            public T Current { get; private set; }
            object IEnumerator.Current { get { return Current; } }

            internal Enumerator(ManagedArray<T> array) {
                index = 0;
                len = array.Count;
                Current = default(T);

                list = array.Elements;
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (index < len) {
                    Current = list[index];
                    index++;
                    return true;
                }
                else {
                    Current = default(T);
                    return false;
                }
            }

            void IEnumerator.Reset() {
                index = 0;
                Current = default(T);
            }
        }
    }
}
