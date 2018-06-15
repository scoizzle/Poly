namespace Poly.Net.Http.V2 {

    public enum FrameType : byte {
        DATA,
        HEADERS,
        PRIORITY,
        RST_STREAM,
        SETTINGS,
        PUSH_PROMISE,
        PING,
        GOAWAY,
        WINDOW_UPDATE,
        CONTINUATION
    }
}