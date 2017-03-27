namespace System {
    static class ByteExtensions {
        public static string ToHexString(this byte C) {
            return ByteArrayExtensions.GetHexString(C);
        }
    }
}
