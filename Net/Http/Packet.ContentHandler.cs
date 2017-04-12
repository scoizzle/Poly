using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;
    using Tcp;

    public class ContentHandler {
        public Packet Packet;
        public Client Client;
        public Stream Content;

        bool isChunkedEnabled;

        public bool HasContent {
            get { return Content?.Length > 0; }
        }

        public ContentHandler(Packet packet) {
            Packet = packet;
            Client = packet.Client;
        }

        public void Reset() {
            if (Content is FileStream)
                Content.Dispose();

            Content = null;
            isChunkedEnabled = false;
        }

        public void Print(string Text) {
            if (Content == null)
                Content = new MemoryStream();

            var Bytes = Encoding.UTF8.GetBytes(Text);
            Content.Write(Bytes, 0, Bytes.Length);
        }

        public Task<bool> Send() {
            return Client.Send(Content);
        }

        public Task<bool> Receive() {
            return Client.Receive(Content, Packet.ContentLength);
        }

        public async Task<bool> SendGzip(Client client) {
            if (!Packet.Gzip)
                return false;

            var NetStream = client.Stream;
            using (var Gzip = new GZipStream(NetStream, CompressionLevel.Fastest, true)) {
                try {
                    await Content.CopyToAsync(Gzip);
                    return true;
                }
                catch { return false; }
            }
        }

        public async Task<bool> ReceiveGZip(Client client) {
            if (!Packet.Gzip)
                return false;
            
            using (var Gzip = new GZipStream(Content, CompressionMode.Decompress, true)) {
                return await client.Receive(Gzip, Packet.ContentLength);
            }
        }

        public async Task<bool> SendFirstChunk(Client client) {
            if (!isChunkedEnabled)
                return false;
            
            Packet.Headers.Clear();
            return true;
        }

        public async Task<bool> SendChunk(Client client, byte[] Data) {
            if (!isChunkedEnabled)
                return false;

            var Size = Data.Length;
            var Hex = Size.ToString("X");

            return await client.SendLine(Hex) &&
                   await client.Send(Data) &&
                   await client.SendLine();
        }

        public async Task<bool> SendFinalChunk(Client client) {
            if (!isChunkedEnabled)
                return false;

            var Trailer = new StringBuilder();
            Packet.GenerateHeaders(Trailer);

            return await client.SendLine("0") &&
                   await client.SendLine(Trailer.ToString());
        }

        public async Task<bool> ReceiveChunked(Client client) {
            while (client.Connected) {
                var hex = await client.ReceiveLineConstrained();

                if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long len)) {
                    if (len == 0) {

                    }
                    else
                    if (await client.Receive(Content, len)) {
                        var Empty = await client.ReceiveLineConstrained();

                        if (Empty.Length > 0)
                            break;
                    }
                    else break;
                }
            }

            return false;
        }


    }
}
