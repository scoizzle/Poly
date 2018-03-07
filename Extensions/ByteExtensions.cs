namespace System {

    public static class ByteExtensions {

        public static string ToHexString(this byte C) {
            return ByteArrayExtensions.GetHexString(C);
        }

        public static bool IsBitSet(this byte Byte, int position) {
            var mask = (1 << position);
            return (Byte & mask) == mask;
        }

        public static byte SetBit(this byte Byte, int position) {
            return (byte)(Byte | (1 << position));
        }

        public static byte UnsetBit(this byte Byte, int position) {
            return (byte)(Byte & ~(1 << position));
        }
    }
}