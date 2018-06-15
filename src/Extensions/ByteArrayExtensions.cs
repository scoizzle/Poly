using Poly.Data;

namespace System {
    using IO;
    using Text;

    public static class ByteArrayExtensions {
        public static Stream GetStream(this byte[] This, bool writable = false) =>
            new MemoryStream(This, writable);
        
        public static string GetString(this byte[] This) =>
            GetString(This, Poly.App.Encoding);

        public static string GetString(this byte[] This, Encoding enc) =>
            enc.GetString(This);

        public static string ToHexString(this byte[] This) =>
            GetHexString(This);

        public static string GetHexString(params byte[] This) {
            var Length = This.Length;
            var Buffer = new char[Length * 2];

            for (int i = 0; i < Length; i++) {
                var idx = i * 2;

                Buffer[idx] = (char)('A' + This[i] >> 4);
                Buffer[idx + 1] = (char)('A' + This[i] & 0xF);
            }

            return new string(Buffer);
        }

        public unsafe static bool CompareSubByteArray(this byte[] left, int left_index, byte[] right, int right_index, int length) {
            if (left == null || left_index < 0 || left_index + length > left.Length)
                return false;

            if (right == null || right_index < 0 || right_index + length > right.Length)
                return false;

            var integers = length / 4;
            var bytes = length % 4;

            fixed (byte* l = left, r = right) 
                return CompareSubByteArrayUnsafeInternal(l + left_index, r + right_index, integers, bytes);
        }

        public unsafe static bool CompareSubByteArrayUnsafeInternal(byte* a, byte* b, int integers, int bytes) {
            while (integers > 0) {
                if (*(int*)(a) != *(int*)(b))
                    return false;

                integers --;
                a += 4;
                b += 4;
            }

            while (bytes > 0) {
                if (*a != *b)
                    return false;

                bytes --;
                a ++;
                b ++;
            }

            return true;
        }

        public unsafe static int FindSubByteArray(this byte[] left, int left_index, int left_length, byte[] right, int right_index, int right_length) {
            if (left == null || left_index < 0 || left_index + left_length > left.Length)
                return -1;

            if (right == null || right_index < 0 || right_index + right_length > right.Length)
                return -1;

            var integers = right_length / 4;
            var bytes = right_length % 4;
            var searchable_bytes = left_length - left_index - right_length;

            fixed (byte* l = left, r = right) {
                byte* a = l + left_index, b = r + right_index;

                while (searchable_bytes-- >= 0) {
                    if (*a == *b)
                    if (CompareSubByteArrayUnsafeInternal(a, b, integers, bytes))
                        return (int)(a - l);

                    a ++;
                }
            }

            return -1;
        }

        public static unsafe bool CopyTo(this byte[] from, int index, byte[] to, int to_index, int count) {
            if (from == null || to == null)
                return false;

            var x = index;
            var y = to_index;
            var x_max = index + count;
            var y_max = to_index + count;

            if (from.Length < x_max)
                return false;

            if (to.Length < y_max)
                return false;

            do {
                to[y] = from[x];
                x++;
                y++;
            }
            while (x != x_max);
            return true;
        }
    }
}