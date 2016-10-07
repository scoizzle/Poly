using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public partial class KeyValueCollection<T> {
        public void Add(string Key, T Value) {
            var Coll = GetCollection(Key.Length);

            if (Coll != null) {
                var S = GetStorageFromCollection(Coll, Key);

                if (S != null) {
                    throw new ArgumentException("An element with the same key already exists.");
                }

                Coll.List.Add(new KeyValuePair(Key, Value));
                Count++;
            }
            else {
                List.Add(Coll = new PairCollection(Key.Length));
                Coll.List.Add(new KeyValuePair(Key, Value));
                Count++;
                return;
            }
        }

        public void remove(string Key) {
            Remove(Key);
        }

        public bool Remove(string Key) {
            var Coll = GetCollection(Key.Length);

            if (Coll != null) {
                var S = GetStorageFromCollection(Coll, Key);

                if (S != null) {
                    Coll.List.Remove(S);
                    Count--;
                    return true;
                }
            }
            return false;
        }

        public void Clear() {
            List.Clear();
            Count = 0;
        }

        public bool ContainsKey(string Key) {
            var Coll = GetCollection(Key.Length);

            if (Coll == null)
                return false;

            var Len = Coll.List.Count;
            for (int i = 0; i < Len; i++) {
                if (string.Compare(Coll.List.Elements[i].Key, Key, StringComparison.Ordinal) == 0) {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValue(string Key, out T Value) {
            var Coll = GetCollection(Key.Length);

            if (Coll != null) {
                return TryGetValueFromCollection(Coll, Key, out Value);
            }

            Value = default(T);
            return false;
        }

        public object get(string Key) {
            return Get(Key);
        }

        public T Get(string Key) {
            var Coll = GetCollection(Key.Length);

            if (Coll != null)
                return GetValueFromCollection(Coll, Key);

            return default(T);
        }

        public void set(string Key, object Value) {
            Set(Key, (T)Value);
        }
        
        public void Set(string Key, T Value) {
            if (Key == null)
                return;

            var Coll = GetCollection(Key.Length);

            if (Coll == null) {
                List.Add(Coll = new PairCollection(Key.Length));
                Coll.List.Add(new KeyValuePair(Key, Value));
                Count++;
                return;
            }

            var Storage = GetStorageFromCollection(Coll, Key);

            if (Storage == null) {
                Count++;
                Coll.List.Add(new KeyValuePair(Key, Value));
            }
            else {
                Storage.Value = Value;
            }
        }

        internal void Set(KeyValuePair Pair) {
            if (Pair == null)
                return;

            var Len = Pair.Key.Length;
            var Coll = GetCollection(Len);

            if (Coll == null) {
                List.Add(Coll = new PairCollection(Len));
                Coll.List.Add(Pair);
                Count++;
                return;
            }

            var Storage = GetStorageFromCollection(Coll, Pair.Key);

            if (Storage == null) {
                Count++;
                Coll.List.Add(Pair);
            }
            else {
                Storage.Value = Pair.Value;
            }
        }

        private KeyValuePair GetStorageFromCollection(PairCollection Coll, string Key) {
            var Len = Coll.List.Count;
            for (int i = 0; i < Len; i++) {
                var Item = Coll.List.Elements[i];

                if (Item == null) {
                    Coll.List.RemoveAt(i--);
                    Len--;
                    continue;
                }

                if (string.Compare(Item.Key, Key, StringComparison.Ordinal) == 0) {
                    return Item;
                }
            }
            return null;
        }

        private T GetValueFromCollection(PairCollection Coll, string Key) {
            var Len = Coll.List.Count;
            for (int i = 0; i < Len; i++) {
                var Item = Coll.List.Elements[i];

                if (string.Compare(Item.Key, Key, StringComparison.Ordinal) == 0) {
                    return Item.Value;
                }
            }

            return default(T);
        }

        private bool TryGetValueFromCollection(PairCollection Coll, string Key, out T Value) {
            var Len = Coll.List.Count;
            for (int i = 0; i < Len; i++) {
                var Item = Coll.List.Elements[i];

                if (string.Compare(Item.Key, Key, StringComparison.Ordinal) == 0) {
                    Value = Item.Value;
                    return true;
                }
            }

            Value = default(T);
            return false;
        }

        private PairCollection GetCollection(int Length) {
            var Len = List.Count;
            for (int i = 0; i < Len; i++) {
                if (List[i].Len == Length)
                    return List[i];
            }
            return null;
        }
    }
}
