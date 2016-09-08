using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Tcp {
	using Http;

	public class Client : TcpClient {
        public static readonly byte[] NewLineBytes = Encoding.Default.GetBytes(App.NewLine);


        public BufferedStreamer Stream { get; private set; }

		public Client() { }
		public Client(IPEndPoint End) { Connect(End); }
		public Client(string Host, int Port) { Connect(Host, Port); }

		public IPEndPoint LocalIPEndPoint {
			get {
				return Client?.LocalEndPoint as IPEndPoint;
			}
		}

		public IPEndPoint RemoteIPEndPoint {
			get {
				return Client?.RemoteEndPoint as IPEndPoint;
			}
		}

        public BufferedStreamer GetStreamer() {
            InitStream();
            return Stream;
        }

        private void InitStream() {
            if (Stream == null)
                Stream = new BufferedStreamer(base.GetStream());
        }

        public async Task<bool> Send(byte[] bytes) {
            try { 
                if (Connected) {
                    InitStream();

                    await Stream.Send(bytes);
                }
            }
            catch { return false; }
            return true;
        }

        public async Task<bool> Send(byte[] bytes, int index, int length) {
            try {
                if (Connected) {
                    InitStream();

                    await Stream.Send(bytes, index, length);
                }
            }
            catch { return false; }
            return true;
        }

        public async Task<bool> Send(string str) {
			return await Send(str, Encoding.Default);
		}

		public async Task<bool> Send(string str, Encoding enc) {
            return await Send(enc.GetBytes(str));
		}

		public async Task<bool> SendLine() {
			return await SendLine(string.Empty, Encoding.Default);
		}

		public async Task<bool> SendLine(string line) {
			return await SendLine(line, Encoding.Default);
		}

		public async Task<bool> SendLine(string line, Encoding enc) {
            try { 
			    if (Connected) {
                    InitStream();

                    if (line != null) {
                        var bytes = enc.GetBytes(line);

                        await Stream.Send(bytes, NewLineBytes);
                    }
                }
            }
            catch { return false; }
            return true;
		}

		public async Task<bool> Receive(Stream storage, long length) {
            if (Connected) {
                InitStream();

                return await Stream.Receive(storage, length);
            }
            return false;
        }

        public async Task<string> ReceiveString(long byteLen) {
            return await ReceiveString(byteLen, Encoding.Default);
        }

        public async Task<string> ReceiveString(long byteLen, Encoding enc) {
            var Out = new MemoryStream();

            if (await Receive(Out, byteLen)) {
                return enc.GetString(Out.ToArray());
            }

            return null;
        }

        public async Task<string> ReceiveStringUntil(byte[] chain, Encoding enc) {
            var Out = new MemoryStream();

            if (await ReceiveUntil(Out, chain)) {
                return enc.GetString(Out.ToArray());
            }

            return null;
        }

		public async Task<string> ReceiveLine() {
			return await ReceiveLine(Encoding.Default);
		}

		public async Task<string> ReceiveLine(Encoding enc) {
			var Out = new MemoryStream();
			if (await ReceiveUntil(Out, NewLineBytes))
                return enc.GetString(Out.ToArray());
            return null;
		}

		public async Task<bool> ReceiveUntil(Stream storage, byte[] chain) {
            if (Connected) {
                InitStream();

                return await Stream.ReceiveUntil(storage, chain);
            }

            return false;
        }

        public static implicit operator Client(Socket socket) {
			return new Client() { Client = socket };
		}
	}
}


