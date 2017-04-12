using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Data {
    public partial class JSON {

        public class Serializer {
            static Dictionary<Type, ISerializer> Cache = new Dictionary<Type, ISerializer>();

            static Serializer() {
                Add(Bool);
                Add(Int);
                Add(Long);
                Add(Float);
                Add(Double);
                Add(String);
            }

            public static void Add<T>(Serializer<T> serial) {
                Cache[typeof(T)] = serial;
            }

            public static ISerializer GetISerializer(Type T) {
                if (Cache.TryGetValue(T, out ISerializer value))
                    return value;

                var genType = typeof(Serializer<>).MakeGenericType(T);

                return Activator.CreateInstance(genType) as ISerializer;
            }

            public static Serializer<T> Get<T>() {
                return GetISerializer(typeof(T)) as Serializer<T>;
            }

            public static Serializer<bool> Bool = new Serializer<bool>(
                b => b ? "true" : "false",
                (StringIterator It, out bool value) => {
                    if (It.Consume("true"))
                        value = true;
                    else if (It.Consume("false"))
                        value = false;
                    else {
                        value = default(bool);
                        return false;
                    }
                    return true;
                });

            public static Serializer<int> Int = new Serializer<int>(
                i => i.ToString(),
                (StringIterator It, out int value) => {
                    var str = GetNumericString(It);

                    if (str != null && int.TryParse(str, out value)) {
                        It.ConsumeSection();
                        return true;
                    }

                    value = default(int);
                    return false;
                });

            public static Serializer<long> Long = new Serializer<long>(
                l => l.ToString(),
                (StringIterator It, out long value) => {
                    var str = GetNumericString(It);

                    if (str != null && long.TryParse(str, out value)) {
                        It.ConsumeSection();
                        return true;
                    }

                    value = default(long);
                    return false;
                });

            public static Serializer<float> Float = new Serializer<float>(
                f => f.ToString(),
                (StringIterator It, out float value) => {
                    var str = GetNumericString(It);

                    if (str != null && float.TryParse(str, out value)) {
                        It.ConsumeSection();
                        return true;
                    }

                    value = default(float);
                    return false;
                });

            public static Serializer<double> Double = new Serializer<double>(
                d => d.ToString(),
                (StringIterator It, out double value) => {
                    var str = GetNumericString(It);

                    if (str != null && double.TryParse(str, out value)) {
                        It.ConsumeSection();
                        return true;
                    }

                    value = default(double);
                    return false;
                });


            public static Serializer<string> String = new Serializer<string>(
                s => string.Concat("\"", s, "\""),
                (StringIterator It, out string value) => {
                    value = It.Extract('"', '"');

                    if (value == null)
                        return false;

                    return true;
                });
            
            static string GetNumericString(StringIterator It) {
                if (It.SelectSection<StringIterator>( _ => It.ConsumeNumericValue())) {
                    return It.ToString();
                }

                return null;
            }
        }
    }

}