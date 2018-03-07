using System.Collections.Generic;
using System.Linq;

namespace Poly.Data {

    public partial class JSON {

        new public object Get(string key_list) {
            return Get(key_list.Split(KeySeperatorCharacter));
        }

        public object Get(string[] key_list) {
            var lastIndex = key_list.Length - 1;

            object value = null;
            JSON current = this;

            for (var i = 0; i <= lastIndex; i++) {
                if (!current.TryGetValue(key_list[i], out value))
                    break;

                if (i == lastIndex)
                    return value;

                if (value is JSON next)
                    current = next;
                else
                    break;
            }

            return null;
        }

        public T Get<T>(string key_list) {
            return Get<T>(key_list.Split(KeySeperatorCharacter));
        }

        public T Get<T>(string[] key_list) {
            var obj = Get(key_list);

            if (obj is T value) {
                return value;
            }

            return default;
        }

        public bool TryGet(string key_list, out object value) {
            value = Get(key_list);
            return value != null;
        }

        new public bool Set(string key_list, object value) {
            return Set(key_list.Split(KeySeperatorCharacter), value);
        }

        public bool Set(IEnumerable<string> key_list, object value) {
            var keys = key_list.ToArray();
            var lastIndex = keys.Length - 1;

            JSON current = this;

            for (var i = 0; i <= lastIndex; i++) {
                var key = keys[i];

                if (i == lastIndex) {
                    current[key] = value;
                    return true;
                }

                if (current.TryGetValue(key, out object raw)) {
                    if (raw is JSON next)
                        current = next;
                    else
                        return false;
                }
                else {
                    var next = new JSON();
                    current.Add(key, next);
                    current = next;
                }
            }

            return false;
        }
    }
}