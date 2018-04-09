using System;
using System.Collections;
using System.Collections.Generic;

namespace Poly.Collections {

    public partial class ManagedArray<T> : IList<T>, IEnumerable<T> {
        int count, length;
        T[] elements;

        public ManagedArray(int len) {
            count = 0;
            length = len;
            elements = new T[len];
        }

        public ManagedArray() : this(4) { }
        
        public T this[int index] {
            get => Get(index);
            set => Set(index, value);
        }

        public int Count => count;
        public int Length => length;

        public bool IsReadOnly => false;

        public T Get(int index) {
            if (index >= 0 && index < elements.Length)
                return elements[index];

            return default;
        }
        
        public bool Set(int index, T value) {
            if (index >= 0 && index < elements.Length) {
                elements[index] = value;
                return true;
            }

            return false;
        }

        public bool Contains(T item) {
            return IndexOf(item) >= 0;
        }

        public void Add(T value) {
            ValidateSizeForInsert();
            Set(count++, value);
        }

        public void Add(T[] value) {
            var length = value.Length;
            ValidateSizeForInsert(Count + length);

            Array.Copy(value, 0, elements, count, length);
            count += length;
        }

        public void Clear() {
            Array.Clear(elements, 0, count);
            count = 0;
        }

        public void CopyTo(T[] array, int arrayIndex) {
            Array.Copy(elements, 0, array, arrayIndex, count);
        }

        public void Insert(int index, T value) {
            if (index < 0)
                return;

            if (index > length) {
                ValidateSizeForInsert(index);
                Set(index, value);
            }
            else {
                ValidateSizeForInsert(count + 1);
                Migrate(from: index, to: index + 1, count: count - index);

                Set(index, value);
                count++;
            }
        }

        public bool Remove(T value) {
            return TryRemoveAt(IndexOf(value));
        }

        public void RemoveAt(int index) {
            TryRemoveAt(index);
        }

        public bool TryRemoveAt(int index) =>
            TryRemoveAt(index, 1);

        public bool TryRemoveAt(int index, int count) {
            var present = this.count;
            if (index < 0 || count <= 0 || index + count > present)
                return false;

            var endOfSegment = index + count;
            if (endOfSegment != present) {
                var elementsRightOfSegment = present - count - index;
                Migrate(from: endOfSegment, to: index, count: elementsRightOfSegment);
            }

            this.count -= count;
            return true;
        }

        public int IndexOf(T value) {
            return IndexOf(_ => Equals(_, value));
        }

        private void ValidateSizeForInsert() {
            if (Count < Length)
                return;

            length *= 2;
            Array.Resize(ref elements, length);            
        }

        private void ValidateSizeForInsert(int minimumIndex) {
            if (minimumIndex > length) {
                Array.Resize(ref elements, minimumIndex);
                length = minimumIndex;
            }
        }

        private void Migrate(int from, int to, int count) {
            Array.Copy(elements, from, elements, to, count);
        }

        public IEnumerable<T> Elements {
            get {
                var elements = this.elements;
                var elements_length = Count;

                for (var i = 0; i < elements.Length && i < elements_length; ++i)
                    yield return elements[i];
            }
        }

        public IEnumerator<T> GetEnumerator() =>
            Elements.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            Elements.GetEnumerator();
    }
}