using System;
using System.Net.Sockets;

namespace System { 
    public static class SocketExtensions {
        private class IOControllers {
            public static int FIONREAD = 0x4004667F;
        }

        public static uint GetPendingByteCount(this Socket sock) {
            byte[] buffer = new byte[sizeof(uint)];
            sock.IOControl(IOControllers.FIONREAD, null, buffer);
            return BitConverter.ToUInt32(buffer, 0);
        }

        public static bool Pending(this Socket sock) {
            if (sock == null || !sock.IsBound)
                return false;

            return sock.Poll(0, SelectMode.SelectRead);
        }
    }
}
