using System;
using System.Text;

namespace Poly.Data {

    public partial class Serializer {
        public static KeyValueCollection<Serializer> Cache = new KeyValueCollection<Serializer>();

        public static Serializer GetCached(string name) {
            return Cache.TryGetValue(name, out Serializer serializer) ? serializer : null;
        }

        public static Serializer GetCached(Type type) {
            if (Cache.TryGetValue(type.Name, out Serializer serializer))
                return serializer;

            return Activator.CreateInstance(typeof(Serializer<>).MakeGenericType(type)) as Serializer;
        }

        public static Serializer<T> GetCached<T>() {
            var name = typeof(T).Name;

            if (Cache.TryGetValue(name, out Serializer serializer))
                return serializer as Serializer<T>;

            return new Serializer<T>();
        }

        public static Serializer<bool> Bool = new Serializer<bool>(
                    (StringBuilder it, bool b) => {
                        it.Append(b ? "true" : "false"); return true;
                    },
                    (StringIterator it, out bool b) => {
                        if (it.ConsumeIgnoreCase("true")) { b = true; return true; }
                        if (it.ConsumeIgnoreCase("false")) { b = false; return true; }
                        return b = false;
                    });

        public static Serializer<string> String = new Serializer<string>(
                    (StringBuilder it, string str) => {
                        it.Append('"').Append(str).Append('"'); return true;
                    },
                    (StringIterator it, out string str) => {
                        if (it.SelectSection('"', '"')) {
                            str = it;
                            it.ConsumeSection();
                            return true;
                        }
                        str = null;
                        return false;
                    });

        public static Serializer<byte> Byte = IntegerSerializer<byte>(byte.TryParse);
        public static Serializer<ushort> UShort = IntegerSerializer<ushort>(ushort.TryParse);
        public static Serializer<uint> UInt = IntegerSerializer<uint>(uint.TryParse);
        public static Serializer<ulong> ULong = IntegerSerializer<ulong>(ulong.TryParse);

        public static Serializer<sbyte> SByte = IntegerSerializer<sbyte>(sbyte.TryParse);
        public static Serializer<short> Short = IntegerSerializer<short>(short.TryParse);
        public static Serializer<int> Int = IntegerSerializer<int>(int.TryParse);
        public static Serializer<long> Long = IntegerSerializer<long>(long.TryParse);

        public static Serializer<float> Float = FloatingSerializer<float>(float.TryParse);
        public static Serializer<double> Double = FloatingSerializer<double>(double.TryParse);

        private delegate bool TryParseDelegate<T>(string str, out T value);

        private static Serializer<T> IntegerSerializer<T>(TryParseDelegate<T> parse) {
            return new Serializer<T>(
                (StringBuilder json, T obj) => {
                    json.Append(obj);
                    return true;
                },
                (StringIterator json, out T obj) => {
                    if (json.SelectSection(_ => _.ConsumeIntegerNumeric())) {
                        if (parse(json, out obj)) {
                            json.ConsumeSection();
                            return true;
                        }

                        json.PopSection();
                    }
                    obj = default(T);
                    return false;
                });
        }

        private static Serializer<T> FloatingSerializer<T>(TryParseDelegate<T> parse) {
            return new Serializer<T>(
                (StringBuilder json, T obj) => {
                    json.Append(obj);
                    return true;
                },
                (StringIterator json, out T obj) => {
                    if (json.SelectSection(_ => _.ConsumeFloatingNumeric())) {
                        if (parse(json, out obj)) {
                            json.ConsumeSection();
                            return true;
                        }
                        json.PopSection();
                    }

                    obj = default(T);
                    return false;
                });
        }
    }
}