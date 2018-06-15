﻿namespace System {
    public static class ByteExtensions {
        public static void SetBit(ref byte Byte, int position) {
            Byte = (byte)(Byte | (1 << position));
        }

        public static void UnsetBit(ref byte Byte, int position) {
            Byte = (byte)(Byte | ~(1 << position));
        }

        public static string ToHexString(this byte C) {
            return ByteArrayExtensions.GetHexString(C);
        }

        public static bool IsBitSet(this byte Byte, int position) =>
            (Byte & (1 << position)) != 0;
    }
}