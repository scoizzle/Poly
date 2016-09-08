using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;
using Poly.Script;

namespace Poly.Net.Http {
    public partial class Request : Packet {
        public Host Host;
        public Client Client;
        public Result Result;

        public bool CompressionEnabled,
                    HeadersOnly;

        public string HeaderString {
            get {
                return Result.GetResponseString();
            }
        }

        public StringBuilder OutputBuilder;

        public Request(Client client) {
            Client = client;
            Result = new Result();
            OutputBuilder = new StringBuilder();
        }

        public void Print(string Content) {
            OutputBuilder.Append(Content);
        }

        public void Prepare() {
            HeadersOnly = string.Compare(Type, "HEAD", StringComparison.Ordinal) == 0;
            CompressionEnabled = AcceptEncoding.Find("gzip") != -1;
        }

        public async void SendFile(string FileName) {
            bool OpenFileDirect = false;
            Cache.Item Cached = null;

            if (!Host.Cache.TryGetValue(FileName, out Cached)) {
                Result.Status = Result.NotFound;
                await Client.Send(HeaderString);
                return;
            }

            if (Cached.LastWriteTime == IfModifiedSince) {
                Result.Status = Result.NotModified;
            }
            else {
                Result.Headers["Last-Modified"] = Cached.LastWriteTime;
                Result.Headers["Content-Length"] = Cached.ContentLength;
                Result.ContentType = Cached.ContentType;

                if (Cached.IsCompressed) {
                    if (CompressionEnabled) {
                        Result.Headers["Vary"] = "Accept-Encoding";
                        Result.Headers["Content-Encoding"] = "gzip";
                    }
                    else OpenFileDirect = true;
                }
            }

            await Client.Send(HeaderString);

            Stream In, Out;

            In = OpenFileDirect ?
                Cached.Info.OpenRead() :
                Cached.Content;

            Out = Client.Stream.Stream;

            try {
                await In.CopyToAsync(Out);
                await Out.FlushAsync();
            }
            catch { }
            finally {
                In.Close();
            }
        }

        public async void Finish() {
            try {
                await Client.Send(HeaderString);

				if (!HeadersOnly && Result.Content != null) {
					var Out = Client.Stream.Stream;

					await Result.Content.CopyToAsync(Out);
					await Out.FlushAsync();
				}
            }
			catch { }
        }
    }
}
