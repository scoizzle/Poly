using System.Collections.Generic;
using System.Linq;

namespace Poly.Data {
    public partial class JSON {
        new public object this[string key] {
            get => Get(key);
            set => Set(key, value);
        }

        public object Get(string key_list) {
            var it = new StringIterator(key_list);
            it.SelectSplitSections(KeySeperatorCharacter);

            return Get(it);
        }

        public object Get(StringIterator it) {
            object value = null;
            JSON current = this;

            do {
                if (!current.TryGetValue(it, out value))
                    break;

                if (it.IsLastSection)
                    return value;

                it.ConsumeSection();
            }
            while (!it.IsDone);

            return null;
        }

        public T Get<T>(string key_list) {
            var it = new StringIterator(key_list);
            it.SelectSplitSections(KeySeperatorCharacter);

            return Get<T>(it);
        }

        public T Get<T>(StringIterator it) {
            return Get(it) is T value ? value : default;
        }

        public bool TryGet(string key_list, out object value) {
            value = Get(key_list);
            return value != null;
        }

        public bool TrySet(string key_list, object value) {
            var it = new StringIterator(key_list);
            it.SelectSplitSections(KeySeperatorCharacter);

            return Set(it, value);
        }

        public bool Set(StringIterator it, object value) {
            JSON current = this;

            do {
                var key = it.ToString();

                if (it.IsLastSection) {
                    current[key] = value;
                    return true;
                }

                if (current.TryGetValue(key, out object obj)) {
                    if (obj is JSON next) {
                        current = next;
                    }
                    else return false;
                }
                else {
                    var next = new JSON();
                    current.Add(key, next);
                    current = next;
                }

                it.ConsumeSection();
            }
            while (!it.IsDone);

            return false;
        }

        public bool TryGetValue<T>(string Key, out T Value) {
            if (base.TryGetValue(Key, out object Val)) {
                if (Val is T value) {
                    Value = value;
                    return true;
                }
            }

            Value = default(T);
            return false;
        }
    }
}