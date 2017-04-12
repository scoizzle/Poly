using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Poly.Data {
    public interface ISerializer {
        Type Type { get; }
        string ISerialize(object raw);
        bool IDeserialize(StringIterator json, out object obj);
    }

    public partial class Serializer<T> : ISerializer {
        delegate object GetDelegate(object obj);
        delegate void SetDelegate(object obj, object val);

        struct Member {
            public GetDelegate get;
            public SetDelegate set;
            public ISerializer serial;
        }

        public delegate string SerializeDelegate(T obj);
        public delegate bool DeserializeDelegate(StringIterator json, out T obj);

        public readonly SerializeDelegate Serialize;
        public readonly DeserializeDelegate Deserialize;

        public Type Type { get; }
        TypeInfo Info;
        KeyValueCollection<Member> Members;

        ISerializer ElementSerializer;

        public Serializer() {
            Type = typeof(T);
            Info = Type.GetTypeInfo();
            JSON.Serializer.Add(this);

            if (Type.IsArray) {
                ElementSerializer = JSON.Serializer.GetISerializer(Type.GetElementType());
                Serialize = getArraySerializer();
                Deserialize = getArrayDeserializer();
            }
            else {
                Members = getMemberList();
                Serialize = getSerializer();
                Deserialize = getDeserializer();
            }
        }

        public Serializer(SerializeDelegate serialize, DeserializeDelegate deserialize) {
            Type = typeof(T);
            Info = Type.GetTypeInfo();
            JSON.Serializer.Add(this);

            Members = getMemberList();
            Serialize = serialize;
            Deserialize = deserialize;
        }

        public string ISerialize(object raw) {
            if (raw is T obj)
                return Serialize(obj);

            return raw?.ToString();
        }

        public bool IDeserialize(StringIterator json, out object obj) {
            Deserialize(json, out T val);
            obj = val;
            return obj != null;
        }

        KeyValueCollection<Member> getMemberList() {
            var list = new KeyValueCollection<Member>();

            foreach (var field in Info.DeclaredFields)
                if (!field.IsStatic && field.IsPublic && !field.IsLiteral)
                list.Add(field.Name, new Member {
                    get = field.GetValue,
                    set = field.SetValue,
                    serial = JSON.Serializer.GetISerializer(field.FieldType)
                });

            foreach (var prop in Info.DeclaredProperties)
                if (prop.CanRead && prop.CanWrite)
                list.Add(prop.Name, new Member {
                    get = prop.GetValue,
                    set = prop.SetValue,
                    serial = JSON.Serializer.GetISerializer(prop.PropertyType)
                });

            return list;
        }

        SerializeDelegate getSerializer() {
            var members = Members.ToArray();
            var lastIndex = members.Length - 1;

            return (obj) => {
                var Output = new StringBuilder();

                Output.Append('{');
                for (int i = 0; i <= lastIndex; i++) {
                    var element = members[i];
                    var value = element.Value.get(obj);
                    var str = element.Value.serial.ISerialize(value);

                    if (!string.IsNullOrEmpty(str)) {
                        if (i != 0) Output.Append(',');

                        Output.Append('"').Append(element.Key).Append('"');
                        Output.Append(':').Append(str);
                    }
                }

                return Output.Append('}').ToString();
            };
        }

        SerializeDelegate getArraySerializer() {
            var serial = ElementSerializer;

            return (obj) => {
                var Array = obj as Array;
                var lastIndex = Array.Length - 1;

                var Output = new StringBuilder();
                Output.Append('[');

                var i = 0;
                foreach (var element in Array) {
                    var str = serial.ISerialize(element);
                    if (!string.IsNullOrEmpty(str)) {
                        if (i++ != 0) Output.Append(',');
                        Output.Append(str);
                    }
                }

                return Output.Append(']').ToString();
            };
        }

        DeserializeDelegate getDeserializer() {
            var String = JSON.Serializer.String;

            return (StringIterator It, out T obj) => {
                obj = (T)Activator.CreateInstance(Type);

                if (It.SelectSection('{', '}')) {
                    while (!It.IsDone()) {
                        It.Consume(char.IsWhiteSpace, C => C == ',');

                        if (String.Deserialize(It, out string Key)) {
                            if (It.Consume(char.IsWhiteSpace, C => C == ':')) {
                                if (Members.TryGetValue(Key, out Member Member)) {
                                    if (Member.serial.IDeserialize(It, out object Value)) {
                                        Member.set(obj, Value);
                                    }
                                }
                            }
                        }
                    }

                    It.ConsumeSection();
                    return true;
                }
                else
                throw new FormatException(string.Format("Unable to Serialize {0} into {1}", It, Type.Name));
            };
        }

        DeserializeDelegate getArrayDeserializer() {
            var serial = ElementSerializer;

            return (StringIterator It, out T obj) => {
                var List = new ManagedArray<object>();

                if (It.SelectSection('[', ']')) {
                    while (!It.IsDone()) {
                        It.ConsumeWhitespace();
                        
                        if (serial.IDeserialize(It, out object elem)) {
                            List.Add(elem);
                        }
                        else {
                            throw new FormatException(string.Format("Unable to Serialize {0} into {1}", It, Type.Name));
                        }

                        It.Consume(char.IsWhiteSpace, C => C == ',');
                    }

                    It.ConsumeSection();

                    var list = List.ToArray();
                    var array = Array.CreateInstance(serial.Type, list.Length);

                    list.CopyTo(array, 0);

                    obj = (T)(object)(array);
                    return true;
                }
                else
                    throw new FormatException(string.Format("Unable to Serialize {0} into {1}", It, Type.Name));
            };
        }
    }
}
