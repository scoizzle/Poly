using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Data {
    public partial class Serializer {
        public static void InitDefaultSerializers() {
            Boolean = new Serializer<bool>(
                (StringBuilder it, bool b) => {
                    it.Append(b ? "true" : "false"); return true;
                },
                (StringIterator it, out bool b) => {
                    if (it.ConsumeIgnoreCase("true")) { b = true; return true; }
                    if (it.ConsumeIgnoreCase("false")) { b = false; return true; }
                    return b = false;
                }); 

            String = new Serializer<string>(
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

            Byte = IntegerSerializer<byte>(byte.TryParse);
            UShort = IntegerSerializer<ushort>(ushort.TryParse);
            UInt = IntegerSerializer<uint>(uint.TryParse);
            ULong = IntegerSerializer<ulong>(ulong.TryParse);

            SByte = IntegerSerializer<sbyte>(sbyte.TryParse);
            Short = IntegerSerializer<short>(short.TryParse);
            Int = IntegerSerializer<int>(int.TryParse);
            Long = IntegerSerializer<long>(long.TryParse);

            Float = FloatingSerializer<float>(float.TryParse);
            Double = FloatingSerializer<double>(double.TryParse);
        }

        public static Serializer<bool> Boolean;
        public static Serializer<string> String;
        public static Serializer<byte> Byte;
        public static Serializer<ushort> UShort;
        public static Serializer<uint> UInt;
        public static Serializer<ulong> ULong;

        public static Serializer<sbyte> SByte;
        public static Serializer<short> Short;
        public static Serializer<int> Int;
        public static Serializer<long> Long;

        public static Serializer<float> Float;
        public static Serializer<double> Double;

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
