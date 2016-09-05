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

        public void Add(T value) {
            ValidateSizeForInsert();
            Elements[count++] = value;
        }

        public void Remove(T value) {
            RemoveAt(IndexOf(value));
        }

        public void RemoveAt(int Index) {
            if (Index < 0 || Index >= count)
                return;

            var Last = count - 1;
            for (var i = Index; i < Last; i++) {
                Elements[i] = Elements[i + 1];
            }

            Elements[Last] = default(T);
            count--;
        }

        public void Clear() {
            for (var i = 0; i < count; i++) {
                Elements[i] = default(T);
            }
            count = 0;
        }

        public int IndexOf(T value) {
            for (var i = 0; i < count; i++) {
                if (Elements[i].Equals(value))
                    return i;
            }
            return -1;
        }

        private void ValidateSizeForInsert() {
            if (Elements.Length == count + 1)
                Array.Resize(ref Elements, Elements.Length * 2);
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
