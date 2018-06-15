namespace Poly.Net.Http.V2 {
    public class FrameFlags {
        public byte Byte;

        public bool END_STREAM {
            get => (Byte & 0b1) == 1;
            set => Byte |= 0b1;
        }

        public bool ACK {
            get => (Byte & 0b1) == 1;
            set => Byte |= 0b1;
        }

        public bool END_HEADERS {
            get => (Byte & 0b100) == 1;
            set => Byte |= 0b100;
        }

        public bool PADDED {
            get => (Byte & 0b1000) == 1;
            set => Byte |= 0b1000;
        }

        public bool PRIORITY {
            get => (Byte & 0b10000) == 1;
            set => Byte |= 0b10000;
        }

        public static implicit operator FrameFlags(byte value) => new FrameFlags { Byte = value };
        public static implicit operator byte(FrameFlags flags) => flags.Byte;
    }
}