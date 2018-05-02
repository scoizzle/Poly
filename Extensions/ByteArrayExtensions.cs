using Poly.Data;

namespace System {
    using IO;
    using Text;

    public static class ByteArrayExtensions {
        public static Stream GetStream(this byte[] This, bool writable = false) {
            return new MemoryStream(This, writable);
        }

        public static string GetString(this byte[] This) {
            return GetString(This, Poly.App.Encoding);
        }

        public static string GetString(this byte[] This, Encoding enc) {
            return enc.GetString(This);
        }

        public static string ToHexString(this byte[] This) {
            return GetHexString(This);
        }

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

        public static int IndexOf(this byte[] left, byte bit, int index) {
            if (left == null || index < 0 || index > left.Length)
                return -1;

            for (; index < left.Length; index ++)
                if (left[index] == bit)
                    return index;
                    
            return -1;
        }

        public static bool CompareSubByteArray(this byte[] left, int leftIndex, byte[] right, int rightIndex, int length) {
            if (left == null || right == null || length < 1)
                return false;

            if (leftIndex < 0 || leftIndex + length > left.Length)
                return false;

            if (rightIndex < 0 || rightIndex + length > right.Length)
                return false;

            do {
                if (left[leftIndex++] != right[rightIndex++])
                    return false;
            }
            while (--length > 0);

            return true;
        }

        public static bool? CompareSubByteArrayPartial(this byte[] left, int leftIndex, byte[] right, int rightIndex, int length) {
            if (left == null || right == null || length < 1)
                return null;

            if (leftIndex < 0 || leftIndex + length > left.Length)
                return null;

            if (rightIndex < 0 || rightIndex + length > right.Length)
                return null;

            if (left[leftIndex++] != right[rightIndex++])
                return null;

            while (--length > 0) {
                if (left[leftIndex++] != right[rightIndex++])
                    return false;
            }

            return true;
        }

        public static int FindSubByteArray(this byte[] left, byte[] right, int index, int lastIndex) {
            if (left == null || right == null)
                return -1;

            while ((index = IndexOf(left, right[0], index)) != -1 && index < lastIndex) {
                if (CompareSubByteArray(left, index, right, 0, right.Length)) {
                    return index;
                }

                index++;
            }

            return -1;
        }

        public static bool CopyTo(this byte[] from, int index, byte[] to, int to_index, int count) {
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