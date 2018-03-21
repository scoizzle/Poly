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

        public static int FindSubByteArray(this byte[] This, int index, int last_index, byte[] sub, int sub_index, int length) {
            var last_possible_index = last_index - length;

            while (index++ < last_possible_index) {
                if (This[index] == sub[sub_index]) {
                    var x = index + 1;
                    var y = sub_index + 1;
                    var f = true;

                    while (x < last_possible_index) {
                        if (This[x] != sub[y]) {
                            f = false;
                            break;
                        }

                        x++;
                        y++;
                    }

                    if (f)
                        return index;

                    index++;
                }
            }

            return -1;
        }

        public static bool? CompareSubByteArray(this byte[] This, int index, int last_index, byte[] sub, int sub_index, int length) {
            var last_possible_index = last_index - length;

            if (This[index] == sub[sub_index]) {
                var x = index + 1;
                var y = sub_index + 1;

                while (x != last_possible_index) {
                    if (This[x] != sub[y])
                        return false;                    

                    x++;
                    y++;

                    if (y == length)
                        return true;
                }
            }

            return null;
        }

        public static bool CopyTo(this byte[] from, int index, byte[] to, int to_index, int count) {
            if (from == null || to == null)
                return false;

            if (from.Length < index + count)
                return false;

            if (to.Length < to_index + count)
                return false;

            var x = index;
            var y = to_index;

            for (int i = 0; i < count; i++) {
                to[y] = from[x];

                x++;
                y++;
            }

            return true;
        }
    }
}