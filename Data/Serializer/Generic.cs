using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    using Collections;
    
    public class Serializer<T> : Serializer {
        public delegate bool SerializeDelegate(StringBuilder json, T obj);
        public delegate bool DeserializeDelegate(StringIterator json, out T obj);

        public SerializeDelegate TrySerialize;
        public DeserializeDelegate TryDeserialize;

        public Serializer() : base(typeof(T)) {
            if (Type.IsArray) {
                TrySerialize = DefaultSerializeArray();
                TryDeserialize = DefaultDeserializeArray();
            }
            else {
                TrySerialize = DefaultSerializeObject();
                TryDeserialize = DefaultDeserializeObject();
            }
        }

        public Serializer(SerializeDelegate serialize, DeserializeDelegate deserialize) : base(typeof(T)) {
            TrySerialize = serialize;
            TryDeserialize = deserialize;
        }

        public string Serialize(T obj) {
            var output = new StringBuilder();

            if (TrySerialize(output, obj))
                return output.ToString();

            return null;
        }

        public T Deserialize(string str) {
            if (TryDeserialize(str, out T result))
                return result;

            return default;
        }

        public override bool SerializeObject(StringBuilder json, object obj) {
            if (obj is T typed)
                return TrySerialize(json, typed);

            return false;
        }

        public override bool DeserializeObject(StringIterator json, out object obj) {
            if (TryDeserialize(json, out T result)) {
                obj = result;
                return true;
            }

            obj = null;
            return false;
        }

        private SerializeDelegate DefaultSerializeObject() {
            var members = TypeInfo.Members.ToArray();
            var lastIndex = members.Length - 1;

            return (StringBuilder json, T obj) => {
                json.Append('{');

                for (var i = 0; i <= lastIndex; i++) {
                    var member = members[i].Value;

                    json.Append('"').Append(member.Name).Append("\":");

                    var value = member.Get(obj);
                    var serialize = member.Serializer.SerializeObject(json, value);

                    if (!serialize)
                        return false;

                    if (i != lastIndex)
                        json.Append(',');
                }

                json.Append('}');
                return true;
            };
        }

        private SerializeDelegate DefaultSerializeArray() {
            var serializer = GetCached(Type.GetElementType());

            return (StringBuilder json, T obj) => {
                var array = obj as Array;
                var lastIndex = array.Length - 1;

                json.Append('[');
                for (var i = 0; i <= lastIndex; i++) {
                    var member = array.GetValue(i);
                    var serialize = serializer.SerializeObject(json, member);

                    if (!serialize)
                        return false;

                    if (i != lastIndex)
                        json.Append(',');
                }

                json.Append(']');
                return true;
            };
        }

        private DeserializeDelegate DefaultDeserializeObject() {
            return (StringIterator json, out T obj) => {
                if (!json.SelectSection('{', '}')) {
                    obj = default(T);
                    return false;
                }

                obj = (T)TypeInfo.CreateInstance();

                if (json.IsDone)
                    return true;

                do {
                    json.ConsumeWhitespace();

                    if (!String.TryDeserialize(json, out string name))
                        return false;

                    if (!TypeInfo.Members.TryGetValue(name, out TypeInformation.Member member))
                        return false;

                    json.ConsumeWhitespace();

                    if (!json.Consume(':'))
                        return false;

                    json.ConsumeWhitespace();

                    if (!member.Serializer.DeserializeObject(json, out object value))
                        return false;

                    member.Set(obj, value);

                    json.ConsumeWhitespace();

                    if (!json.Consume(',')) {
                        json.ConsumeSection();
                        break;
                    }
                }
                while (!json.IsDone);

                return true;
            };
        }

        private DeserializeDelegate DefaultDeserializeArray() {
            var elementType = Type.GetElementType();
            var serializer = GetCached(elementType);

            return (StringIterator json, out T obj) => {
                if (!json.SelectSection('[', ']'))
                    goto formatException;

                var list = new List<object>();

                if (!json.IsDone) {
                    do {
                        json.ConsumeWhitespace();

                        if (serializer.DeserializeObject(json, out object element))
                            list.Add(element);
                        else
                            goto formatException;

                        if (!json.Consume(',')) {
                            json.ConsumeSection();
                            break;
                        }
                    }
                    while (!json.IsDone);
                }

                var count = list.Count;
                var array = Array.CreateInstance(elementType, count);

                if (count > 0)
                    Array.Copy(list.ToArray(), array, count);

                obj = (T)(object)(array);
                return true;

                formatException:
                throw new FormatException($"Unable to deserialize {json} into {elementType.Name}");
            };
        }
    }
}