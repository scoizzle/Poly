using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public class ManagedArray<T> : IEnumerable<T> {
        private int count;
        public T[] Elements;

        public int Count { get { return count; } }

        public T this[int index]
        {
            get
            {
                if (index >= 0 && index < count)
                    return Elements[index];
                return default(T);
            }
            set
            {
                if (index >= 0 && index < count)
                    Elements[index] = value;
            }
        }

        public ManagedArray() : this(4) { }

        public ManagedArray(int baseLength) {
            Elements = new T[baseLength];
        }

        public ManagedArray(T[] baseArray) {
            Elements = baseArray;
            count = Elements.Length;
        }

        public ManagedArray(ManagedArray<T> Base) {
            Elements = new T[Base.count == 0 ? 4 : Base.count];
            Base.CopyTo(this);
        }

        public void CopyTo(ManagedArray<T> destination) {
            destination.ValidateSizeForInsert(count + destination.count);

            Array.Copy(Elements, 0, destination.Elements, destination.count, count);
            destination.count += count;
        }

        public void Add(T value) {
            ValidateSizeForInsert();
            Elements[count] = value;
            count++;
        }

        public void Add(T[] value) {
            var length = value.Length;
            ValidateSizeForInsert(count + length);

            Array.Copy(value, 0, Elements, count, length);
            count += length;
        }

        public void Remove(T value) {
            RemoveAt(IndexOf(value));
        }

        public bool RemoveAt(int Index) {
            if (Index < 0 || Index >= count)
                return false;

            if (Index == count - 1) {
                Elements[Index] = default(T);
                count--;
                return true;
            }

            var End = count;
            var Post = End - Index - 1;
            
            Array.Copy(Elements, Index + 1, Elements, Index, Post);

            Elements[End] = default(T);
            count--;
            return true;
        }

        public void RemoveAt(int Index, int Count) {
            if (Index < 0 || Count <= 0 || Index + Count > count)
                return;

            var to_move = count - Count;
            var chunk_end = Index + Count;

            Array.Copy(Elements, chunk_end, Elements, Index, to_move - Index);
            Array.Clear(Elements, count, Elements.Length - count);

            count = to_move;
        }

        public void Clear() {
            Array.Clear(Elements, 0, count);
            count = 0;
        }

        public int IndexOf(T value) {
            for (var i = 0; i < count; i++) {
                if (Elements[i].Equals(value))
                    return i;
            }
            return -1;
        }

        public void Constrain() {
            if (count < Elements.Length)
                Array.Resize(ref Elements, count);
        }

        private void ValidateSizeForInsert() {
            if (Elements.Length <= count + 1)
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
                len = array.count;
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
