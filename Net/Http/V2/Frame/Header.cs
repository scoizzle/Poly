namespace Poly.Net.Http.V2 {

    public struct Header {
        public uint Length;
        public byte Type;
        public byte Flags;
        public uint StreamId;

        public static bool Encode(Header header, IO.MemoryBuffer buffer) {
            var len = header.Length;
            var id = header.StreamId;

            return buffer.Read(
                (byte)(len >> 16),
                (byte)(len >> 8),
                (byte)(len),
                (header.Type),
                (header.Flags),
                (byte)(id >> 24),
                (byte)(id >> 16),
                (byte)(id >> 8),
                (byte)(id)
            );
        }

        public static bool Decode(IO.MemoryBuffer buffer, out Header header) {
            var array = new byte[9];

            if (buffer.Write(array, 0, 9))
                return Decode(array, out header);

            header = default(Header);
            return false;
        }

        public static bool Encode(Header header, out byte[] bytes) {
            bytes = new byte[9];

            var len = header.Length;
            var id = header.StreamId;

            bytes[0] = (byte)(len >> 16);
            bytes[1] = (byte)(len >> 8);
            bytes[2] = (byte)(len);

            bytes[3] = (header.Type);
            bytes[4] = (header.Flags);

            bytes[5] = (byte)(id >> 24);
            bytes[6] = (byte)(id >> 16);
            bytes[7] = (byte)(id >> 8);
            bytes[8] = (byte)(id);

            return true;
        }

        public static bool Decode(byte[] bytes, out Header header) {
            if (bytes.Length < 9) {
                header = default(Header);
                return false;
            }

            var length = 0u +
                bytes[0] << 16 +
                bytes[1] << 8 +
                bytes[2];

            var type = bytes[3];
            var flags = bytes[4];

            var stream_id = 0u +
                bytes[5] << 24 +
                bytes[6] << 16 +
                bytes[7] << 8 +
                bytes[8];

            header = new Header {
                Length = length,
                Type = type,
                Flags = flags,
                StreamId = stream_id
            };

            return true;
        }
    }
}