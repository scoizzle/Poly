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
        public static readonly byte[] NewLineBytes = Encoding.UTF8.GetBytes(App.NewLine);


        public BufferedStreamer Stream { get; private set; }

		public Client() { Client.Blocking = false; }
		public Client(string Host, int Port) { ConnectAsync(Host, Port); }

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

		public bool Send(byte[] bytes) {
            try { 
                if (Connected) {
                    InitStream();

                    return Stream.Send(bytes, 0, bytes.Length);
                }
            }
            catch { return false; }
            return true;
        }

        public bool Send(byte[] bytes, int index, int length) {
            try {
                if (Connected) {
                    InitStream();

                    return Stream.Send(bytes, index, length);
                }
            }
            catch { return false; }
            return true;
        }

        public bool Send(string str) {
			return Send(str, Encoding.UTF8);
		}

		public bool Send(string str, Encoding enc) {
            return Send(enc.GetBytes(str));
		}

		public bool SendLine() {
			return SendLine(string.Empty, Encoding.UTF8);
		}

		public bool SendLine(string line) {
			return SendLine(line, Encoding.UTF8);
		}

		public bool SendLine(string line, Encoding enc) {
            try { 
			    if (Connected) {
                    InitStream();

                    if (line != null) {
                        var bytes = enc.GetBytes(line);

                        return Stream.Send(bytes, NewLineBytes);
                    }
                }
            }
            catch { return false; }
            return true;
		}

		public bool Receive(Stream storage, long length) {
            if (Connected) {
                InitStream();

                return Stream.Receive(storage, length);
            }
            return false;
        }

        public string ReceiveString(long byteLen) {
			return ReceiveString(byteLen, Encoding.UTF8);
        }

        public string ReceiveString(long byteLen, Encoding enc) {
            var Out = new MemoryStream();

            if (Receive(Out, byteLen)) {
                return enc.GetString(Out.ToArray());
            }

            return null;
        }

        public string ReceiveStringUntil(byte[] chain, Encoding enc) {
            var Out = new MemoryStream();

            if (ReceiveUntil(Out, chain)) {
                return enc.GetString(Out.ToArray());
            }

            return null;
        }

		public string ReceiveLine() {
			return ReceiveLine(Encoding.UTF8);
		}

		public string ReceiveLine(Encoding enc) {
			var Out = new MemoryStream();
			if (ReceiveUntil(Out, NewLineBytes))
                return enc.GetString(Out.ToArray());
            return null;
		}

		public bool ReceiveUntil(Stream storage, byte[] chain) {
            if (Connected) {
                InitStream();

                return Stream.ReceiveUntil(storage, chain);
            }

            return false;
        }

        public static implicit operator Client(Socket socket) {
			return new Client() { Client = socket };
		}
	}
}


