namespace Poly.Net.Http.V2 {

    public enum Flags : byte {
        END_STREAM = 1,
        ACK = 1,
        END_HEADERS = 3,
        PADDED = 4,
        PRIORITY = 6
    }
}