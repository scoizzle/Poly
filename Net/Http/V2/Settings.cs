namespace Poly.Net.Http.V2 {

    public class Settings {

        private enum SettingsFrameOptions : ushort {
            HEADER_TABLE_SIZE = 1,
            ENABLE_PUSH,
            MAX_CONCURRENT_STREAMS,
            INITIAL_WINDOW_SIZE,
            MAX_FRAME_SIZE,
            MAX_HEADER_LIST_SIZE
        }

        public int HeaderTableSize = 4096;
        public bool EnablePush = true;
        public int MaxConcurrentStreams = 100;
        public int InitialWindowSize = 65535;
        public int MaxFrameSize = 16384;
        public int MaxHeaderListSize = int.MaxValue;
    }
}