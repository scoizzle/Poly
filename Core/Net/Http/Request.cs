using System;
using System.Threading.Tasks;
using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Http {
    public class Request {
        public delegate Task<bool> Handler(Request Req);

        public Host Host;
        public Client Client;
        public Packet Packet;

        public JSON Arguments;

        public bool CompressionEnabled,
                    HeadersOnly;

        public Request(Client client, Packet packet, Host host) {
            Host = host;
            Client = client;
            Packet = packet;

            Arguments = new JSON();
        }

        public void Reset() {
            Arguments.Clear();
            Packet.Reset();
        }

        public void Prepare() {
            HeadersOnly = Packet.Method.Compare("HEAD", 0);
            CompressionEnabled = (Packet.Headers["Accept-Encoding"] as string)?.Find("gzip") != -1;
        }

		public Task<bool> Send(string Status) {
			return Result.Send(Client, Status);
		}

		public Task<bool> Send(string Status, string Content) {
			return Result.Send(Client, Status, Content);
		}

        public Task<bool> SendFile(string FileName) {
            Cache.Item Cached;

            if (Host.Cache.TryGetValue(FileName, out Cached))
                return SendFile(Cached);

            return Result.Send(Client, Result.NotFound);
        }

        public Task<bool> SendFile(Cache.Item Cached) {
            Result Result;

            if (Cached.LastWriteTime.Compare(Packet.Headers["Last-Modified"], 0)) {
                Result = new Result(Result.NotModified);
            }
            else {
                Result = new Result(Result.Ok);

                Result["Last-Modified"] = Cached.LastWriteTime;
                Result.ContentType = Cached.ContentType;

                if (Cached.IsCompressed) {
                    if (CompressionEnabled) {
                        Result["Vary"] = "Accept-Encoding";
                        Result["Content-Encoding"] = "gzip";
                    }
                }

                Result.Content = Cached.GetContent(CompressionEnabled);
            }

            return Result.Send(Client, HeadersOnly);
        }
    }
}
