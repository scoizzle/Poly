using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Net.Http.V2.HPACK {
    using Collections;

    public class Encoder {
        private enum BitTypes : byte {
            FieldLiteral = 0,
            NeverIndexedFieldLiteral = 16,
            DynamicTableSizeUpdate = 32,
            IndexedFieldLiteral = 64,
            IndexedField = 128
        }

        private static readonly byte[] MaxValues = {
            1, 3, 7, 15, 31, 63, 127, 254
        };

        public HeaderTable Table;

        public Encoder(Settings settings) {
            Table = new HeaderTable(settings.HeaderTableSize);
        }

        public byte[] Encode(int number, byte N) {
            var max = MaxValues[N - 1];

            if (number < max)
                return new [] { (byte)(number) };

            var result = new List<byte>() {
                max
            };

            number -= max;
            while (number >= 128) {
                result.Add(
                    (byte)((number % 128) + 128)
                    );

                number /= 128;
            }

            result.Add(
                (byte)number
                );

            return result.ToArray();
        }

        public int Decode(byte[] bytes, int N) {
            int index = 0;

            return Decode(bytes, ref index, bytes.Length, N);
        }

        public int Decode(byte[] bytes, ref int bytes_index, int bytes_length, int N) {
            var max = MaxValues[N - 1];
            var B = bytes[bytes_index] & max;
            var result = B;
            var M = 1;

            if (B == max) {
                for (bytes_index++; bytes_index < bytes_length; bytes_index++, M *= 128) {
                    B = bytes[bytes_index];
                    result += (B & 0b01111111) * M;

                    if ((B & 0b10000000) == 0)
                        break;
                }
            }

            return result;
        }

        private void Encode_String(List<byte> result, string text, bool huffman = true) {
            if (huffman) {
                if (HuffmanEncoding.Encode(text.ToCharArray(), out byte[] encoded_text)) {
                    var encoded_length = Encode(encoded_text.Length, 7);
                    SetBitFlag(ref encoded_length[0], 0b10000000);

                    result.AddRange(encoded_length);
                    result.AddRange(encoded_text);
                }
            }
            else {
                result.AddRange(Encode(text.Length, 7));
                result.AddRange(App.Encoding.GetBytes(text));
            }
        }

        private string Decode_String(byte[] bytes, ref int bytes_index, int bytes_length) {
            var huffman = HasBitFlag(bytes[bytes_index], 0b10000000);
            var length = Decode(bytes, ref bytes_index, bytes_length, 7);

            if (length > (bytes_length - bytes_index))
                throw new IndexOutOfRangeException("Decoded length greater than header byte count.");

            bytes_index++;

            string result = null;
            if (huffman) {
                if (HuffmanEncoding.Decode(bytes, ref bytes_index, length, out char[] un_huffed)) 
                    result = new string(un_huffed);
            }
            else {
                result = App.Encoding.GetString(bytes, bytes_index, length);
            }

            Log.Debug("Decode_String: Huffman = {0} Length = {1} Value = {2}", huffman, result.Length, result);

            bytes_index += length;
            return result;
        }

        public bool Encode(Dictionary<string, string> headers, out byte[] bytes) {
            var result = new List<byte>();

            foreach (var pair in headers) {
                if (!Encode_Header(result, pair.Key, pair.Value)) {
                    bytes = null;
                    return false;
                }
            }

            bytes = result.ToArray();
            return true;
        }

        private bool Encode_Header(List<byte> result, string key, string value) {
            var index = Table.GetIndex(key, value);

            if (index == 0) {
                return Encode_New_Indexed_Field_Literal(result, key, value);
            }
            else
            if (index < 0) {
                return Encode_Indexed_Field(result, index * -1);
            }
            else {
                return Encode_Indexed_Field_Literal(result, index, key, value);
            }
        }

        private bool Encode_Indexed_Field(List<byte> result, int index) {
            Log.Debug("Encode_Indexed_Field Index = {0}", index);

            var encoded_index = Encode(index, 7);

            SetBitType(ref encoded_index[0], BitTypes.IndexedField);
            result.AddRange(encoded_index);
            return true;
        }

        private bool Encode_New_Indexed_Field_Literal(List<byte> result, string key, string value) {
            Log.Debug("Encode_New_Indexed_Field_Literal Key = {0} Value = {1}", key, value);

            result.Add((byte)BitTypes.IndexedFieldLiteral);

            Encode_String(result, key);
            Encode_String(result, value);

            Table.Add(key, value);
            return true;
        }

        private bool Encode_Indexed_Field_Literal(List<byte> result, int index, string key, string value) {
            Log.Debug("Encode_Indexed_Field_Literal Index = {0} Value = {1}", index, value);

            var encoded_index = Encode(index, 6);
            SetBitType(ref encoded_index[0], BitTypes.IndexedFieldLiteral);
            result.AddRange(encoded_index);

            Encode_String(result, value);

            Table.Add(key, value);
            return true;
        }

        public bool Decode(out Dictionary<string, string> headers, byte[] bytes) {
            var index = 0;
            return Decode(out headers, bytes, ref index, bytes.Length);
        }

        public bool Decode(out Dictionary<string, string> headers, byte[] bytes, ref int byte_index, int bytes_length) {
            headers = new Dictionary<string, string>();

            while (byte_index < bytes_length) {
                var b = bytes[byte_index];
                var type = GetBitType(b);

                switch (type) {
                    case BitTypes.IndexedField: {
                            var index = Decode(bytes, ref byte_index, bytes_length, 7);

                            if (!Decode_Indexed_Field(headers, index))
                                return false;

                            break;
                        }

                    case BitTypes.IndexedFieldLiteral: {
                            var index = Decode(bytes, ref byte_index, bytes_length, 6);
                            byte_index++;

                            if (index == 0) {
                                var key = Decode_String(bytes, ref byte_index, bytes_length);
                                var value = Decode_String(bytes, ref byte_index, bytes_length);

                                if (!Decode_New_Indexed_Field_Literal(headers, key, value))
                                    return false;
                            }
                            else {
                                var value = Decode_String(bytes, ref byte_index, bytes_length);

                                if (!Decode_Indexed_Field_Literal(headers, index, value))
                                    return false;
                            }

                            break;
                        }

                    case BitTypes.FieldLiteral: {
                            var index = Decode(bytes, ref byte_index, bytes_length, 4);
                            byte_index++;

                            if (index == 0) {
                                var key = Decode_String(bytes, ref byte_index, bytes_length);
                                var value = Decode_String(bytes, ref byte_index, bytes_length);

                                if (!Decode_New_Field_Literal(headers, key, value))
                                    return false;
                            }
                            else {
                                var value = Decode_String(bytes, ref byte_index, bytes_length);

                                if (!Decode_Field_Literal(headers, index, value))
                                    return false;
                            }

                            break;
                        }

                    case BitTypes.NeverIndexedFieldLiteral: {
                            var index = Decode(bytes, ref byte_index, bytes_length, 4);
                            byte_index++;

                            if (index == 0) {
                                var key = Decode_String(bytes, ref byte_index, bytes_length);
                                var value = Decode_String(bytes, ref byte_index, bytes_length);

                                if (!Decode_New_Field_Literal(headers, key, value))
                                    return false;
                            }
                            else {
                                var value = Decode_String(bytes, ref byte_index, bytes_length);

                                if (!Decode_Field_Literal(headers, index, value))
                                    return false;
                            }

                            break;
                        }

                    case BitTypes.DynamicTableSizeUpdate: {
                            var size = Decode(bytes, ref byte_index, bytes_length, 5);

                            if (!Table.SetMaxSize(size))
                                return false;

                            break;
                        }
                }

                byte_index++;
            }
            return true;
        }

        private bool HasBitFlag(byte b, byte flag) {
            return (b & flag) == flag;
        }

        private void SetBitFlag(ref byte b, byte flag) {
            b |= flag;
        }

        private BitTypes GetBitType(byte b) {
            if (HasBitFlag(b, 128))
                return BitTypes.IndexedField;

            if (HasBitFlag(b, 64))
                return BitTypes.IndexedFieldLiteral;

            if (HasBitFlag(b, 32))
                return BitTypes.DynamicTableSizeUpdate;

            if (HasBitFlag(b, 16))
                return BitTypes.NeverIndexedFieldLiteral;

            return BitTypes.FieldLiteral;
        }

        private void SetBitType(ref byte b, BitTypes type) {
            switch (type) {
                case BitTypes.IndexedField:
                    SetBitFlag(ref b, 128);
                    return;

                case BitTypes.IndexedFieldLiteral:
                    SetBitFlag(ref b, 64);
                    return;

                case BitTypes.DynamicTableSizeUpdate:
                    SetBitFlag(ref b, 32);
                    return;

                case BitTypes.NeverIndexedFieldLiteral:
                    SetBitFlag(ref b, 16);
                    return;
            }
        }

        private bool Decode_Indexed_Field(Dictionary<string, string> headers, int index) {
            var header = Table[index];

            Log.Debug("Decode_Indexed_Field: header = {0}", header);

            if (header == null)
                return false;

            headers[header.Key] = header.Value;
            return true;
        }

        private bool Decode_Field_Literal(Dictionary<string, string> headers, int index, string value) {
            var header = Table[index];

            Log.Debug("Decode_Field_Literal: Key = {0} Value = {1}", header?.Key, value);

            if (header == null)
                return false;

            headers[header.Key] = value;
            return true;
        }

        private bool Decode_New_Field_Literal(Dictionary<string, string> headers, string key, string value) {
            Log.Debug("Decode_New_Field_Literal: Key = {0} Value = {1}", key, value);

            headers[key] = value;
            return true;
        }

        private bool Decode_Indexed_Field_Literal(Dictionary<string, string> headers, int index, string value) {
            var header = Table[index];

            Log.Debug("Decode_Indexed_Field_Literal: Key = {0} Value = {1}", header?.Key, value);

            if (header == null)
                return false;

            headers[header.Key] = value;
            return Table.Add(header.Key, value) != null;
        }

        private bool Decode_New_Indexed_Field_Literal(Dictionary<string, string> headers, string key, string value) {
            Log.Debug("Decode_New_Indexed_Field_Literal: Key = {0} Value = {1}", key, value);
            headers[key] = value;
            return Table.Add(key, value) != null;
        }
    }
}