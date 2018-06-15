namespace Poly.Net.Http.V2 {

    public class FrameHeader {
        public uint Length;
        public FrameType Type;
        public FrameFlags Flags;
        public uint StreamId;

        public static bool Encode(FrameHeader header, IO.MemoryBuffer buffer) {
            var len = header.Length;
            var id = header.StreamId;

            return buffer.Read(
                (byte)(len >> 16),
                (byte)(len >> 8),
                (byte)(len),
                (byte)(header.Type),
                (byte)(header.Flags),
                (byte)(id >> 24),
                (byte)(id >> 16),
                (byte)(id >> 8),
                (byte)(id)
            );
        }

        public static bool Decode(IO.MemoryBuffer buffer, out FrameHeader header) {
            var array = new byte[9];

            if (buffer.Write(array, 0, 9))
                return Decode(array, out header);

            header = default(FrameHeader);
            return false;
        }

        public static bool Encode(FrameHeader header, out byte[] bytes) {
            bytes = new byte[9];

            var len = header.Length;
            var id = header.StreamId;

            bytes[0] = (byte)(len >> 16);
            bytes[1] = (byte)(len >> 8);
            bytes[2] = (byte)(len);

            bytes[3] = (byte)(header.Type);
            bytes[4] = (byte)(header.Flags);

            bytes[5] = (byte)(id >> 24);
            bytes[6] = (byte)(id >> 16);
            bytes[7] = (byte)(id >> 8);
            bytes[8] = (byte)(id);

            return true;
        }

        public static bool Decode(byte[] bytes, out FrameHeader header) {
            if (bytes.Length < 9) {
                header = default(FrameHeader);
                return false;
            }

            var length = 0u +
                bytes[0] << 16 +
                bytes[1] << 8 +
                bytes[2];

            var type = (FrameType)(bytes[3]);
            var flags = (FrameFlags)(bytes[4]);

            var stream_id = 0u +
                bytes[5] << 24 +
                bytes[6] << 16 +
                bytes[7] << 8 +
                bytes[8];

            header = new FrameHeader {
                Length = length,
                Type = type,
                Flags = flags,
                StreamId = stream_id
            };

            return true;
        }
    }
}