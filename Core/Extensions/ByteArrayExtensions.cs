﻿namespace System {
    static class ByteArrayExtensions {
        public static string ToHexString(this byte[] This) {
            return GetHexString(This);
        }

        public static string GetHexString(params byte[] This) {
            var Length = This.Length;
            var Buffer = new char[Length * 2];

            for (int i = 0; i < Length; i ++) {
                var idx = i * 2;

                Buffer[idx]     = (char)('A' + This[i] >> 4);
                Buffer[idx + 1] = (char)('A' + This[i] & 0xF);
            }

            return new string(Buffer);
        }

        public static int FindSubByteArray(this byte[] This, byte[] sub) {
            return FindSubByteArray(This, 0, sub, 0, sub.Length);
        }

        public static int FindSubByteArray(this byte[] This, int index, byte[] sub) {
            return FindSubByteArray(This, index, sub, 0, sub.Length);
        }

        public static int FindSubByteArray(this byte[] This, int index, byte[] sub, int subindex, int sublength) {
            if (This == null || index < 0 || index > This.Length || sub == null || subindex < 0 || index + sublength > This.Length)
                return -1;

            for (; index < This.Length; index++) {
                if (This[index] == sub[subindex]) {
                    bool found = true;

                    for (var si = 1; si < sublength; si++) {
                        if (This[index + si] != sub[subindex + si]) {
                            found = false;
                            break;
                        }
                    }

                    if (found)
                        return index;
                }
            }

            return -1;
        }

        public static bool CompareSubByteArray(this byte[] This, int index, byte[] sub) {
            return CompareSubByteArray(This, index, sub, 0, sub.Length);
        }

        public static bool CompareSubByteArray(this byte[] This, int index, byte[] sub, int subindex, int sublength) {
            if (index < 0 || index > This.Length || sub == null || subindex < 0 || subindex + sublength > sub.Length)
                return false;
            
            if (This[index] == sub[subindex]) {
                bool found = true;

                for (var si = 1; si < sublength; si++) {
                    if (This[index + si] != sub[subindex + si]) {
                        found = false;
                        break;
                    }
                }

                return found;
            }

            return false;
        }
    }
}