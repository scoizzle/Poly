using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace System {
    public static class StreamExtensions {
        public static string GetString(this Stream This) {
            return GetString(This, Encoding.Default);
        }

        public static string GetString(this Stream This, Encoding Enc) {
            var Buffer = new byte[This.Length];
            This.Position = 0;
            This.Write(Buffer, 0, Buffer.Length);
            var str = Enc.GetString(Buffer);
            Buffer = null;
            return str;
        }

        public static void Write(this Stream This, byte[] bytes) {
            This?.Write(bytes, 0, bytes.Length);
        }

        public static void Write(this Stream This, string Text) {
            Write(This, Text, Encoding.Default);
        }

        public static void WriteLine(this Stream This, string Line) {
            WriteLine(This, Line, Encoding.Default);
        }

        public static void Write(this Stream This, string Text, Encoding Encoding) {
            Write(This, Encoding.GetBytes(Text));
        }

        public static void WriteLine(this Stream This, string Line, Encoding Encoding) {
            Write(This, Encoding.GetBytes(Line + "\r\n"));
        }

        public static async Task ReadAsync(this Stream This, byte[] bytes) {
            await This.ReadAsync(bytes, 0, bytes.Length);
        }

        public static async Task ReadAsync(this Stream This, byte[] bytes, int length) {
            await This.ReadAsync(bytes, 0, length);
        }
    }
}
