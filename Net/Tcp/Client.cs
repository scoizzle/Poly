using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Tcp {
	using Http;

	public class Client {
        public static readonly byte[] NewLineBytes = Encoding.UTF8.GetBytes(App.NewLine);
        public static readonly byte[] EmptyBytes = new byte[0];
        public static int DefaultBufferSize = 8 * 1024;

        protected byte[] Buffer;
        
        public Client() : this(new Socket(SocketType.Stream, ProtocolType.Tcp)) { }

        public Client(Socket sock) {
            Socket = sock;

            Socket.ReceiveTimeout = 5000;
            Socket.SendTimeout = 5000;

            Stream = new NetworkStream(sock);
            Buffer = new byte[DefaultBufferSize];
        }

        public Client(Socket sock, bool secure) : this(sock) {
            if (secure) {
                Secure = true;
                Stream = SecureStream = new SslStream(Stream);
            }
        }

        ~Client() {
            Socket?.Dispose();
        }

        public Socket Socket { get; protected set; }
        public Stream Stream { get; protected set; }
        public SslStream SecureStream { get; protected set; }

        public long TotalBytesConsumed { get; private set; }

        public int Position { get; private set; }
        public int Length { get; private set; }
        public bool Secure { get; private set; }

        public int Available { get { return Length - Position; } }

        public int ReceiveTimeout {
            get { return Socket?.ReceiveTimeout ?? 5000; }
            set { Socket.ReceiveTimeout = Stream.ReadTimeout = value; }
        }

        public int SendTimeout {
            get { return Socket?.SendTimeout ?? 5000; }
            set { Socket.SendTimeout = value; }
        }

        public IPEndPoint LocalIPEndPoint { get { return Socket?.LocalEndPoint as IPEndPoint; } }
		public IPEndPoint RemoteIPEndPoint { get { return Socket?.RemoteEndPoint as IPEndPoint; } }

        public bool Connected {
            get { return Socket?.Connected == true; }
        }
        
        public async Task<bool> Connect(EndPoint ep) {
            try {
                await Socket.ConnectAsync(ep);

                if (Socket.Connected) {
                    Stream = new NetworkStream(Socket);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public async Task<bool> Connect(string host, int port) {
            try {
                var get_addresses = Dns.GetHostAddressesAsync(host);
                var addresses = await get_addresses;

                if (addresses.Length == 0) return false;
                var get_connection = Connect(
                    new IPEndPoint(
                        addresses.First(),
                        port
                ));

                var connected = await get_connection;
                if (connected) {
                    if (Secure) {
                        SecureStream = new SslStream(
                            new NetworkStream(Socket),
                            false,
                            new RemoteCertificateValidationCallback(
                                SecureValidationCallback
                        ));

                        var get_authentication = SecureStream.AuthenticateAsClientAsync(host);
                        await get_authentication;

                        Stream = SecureStream;
                    }

                    return true;
                }
            }
            catch { }
            
            return false;
        }

        public Task<bool> Connect(IPAddress addr, int port) {
            return Connect(new IPEndPoint(addr, port));
        }

        public void Close() {
            Socket.Shutdown(SocketShutdown.Both);
        }

        public async Task<bool> IsConnected() {
            return Socket.Connected && await Send(EmptyBytes);
        }
        
        public async Task<bool> ReadyToRead() {
            if (Available == 0) await UpdateBuffer();
            return Available > 0;
        }

        public async Task<bool> Send(Stream Content) {
            try {
                await Content.CopyToAsync(Stream, DefaultBufferSize);
                return true;
            }
            catch (Exception) { return false; }
        }

        public async Task<bool> Send(byte[] bytes, int index = 0, int length = -1) {
            if (Stream == null) return false;
            if (length == -1) length = bytes.Length;

            try {
                await Stream.WriteAsync(bytes, index, length);
                return true;
            }
            catch { return false; }
        }

        public Task<bool> Send(string str) {
            return Send(str, Encoding.UTF8);
		}

		public Task<bool> Send(string str, Encoding enc) {
            return Send(enc.GetBytes(str));
        }

        public Task<bool> SendLine() {
            return Send(App.NewLine);
        }

        public Task<bool> SendLine(string Line) {
            return Send(string.Concat(Line, App.NewLine));
        }

        public Task<bool> SendLine(string Line, Encoding enc) {
            return Send(string.Concat(Line, App.NewLine), enc);
        }

        public bool Consume(byte[] chain) {
            if (Available == 0) return false;

            var Len = chain.Length;
            var b = Buffer;

            for (var i = 0; i < Len; i++) {
                if (b[Position + i] != chain[i])
                    return false;
            }

            Consume(Len);
            return true;
        }

        public async Task<bool> Receive(Stream Storage, long Length) {
            if (Stream == null) return false;
            var B = Buffer;
            var Len = 0;

            try {
                while (Length > 0) {
                    if (Available == 0) await UpdateBuffer();

                    Len = (int)(Math.Min(Length, Available));
                    await Storage.WriteAsync(B, Position, Len);

                    Length -= Len;
                    Consume(Len);
                }

                await Storage.FlushAsync();
                return true;
            }
            catch (Exception) { return false; }
        }

        public async Task<bool> ReceiveUntil(Stream Storage, byte[] Chain, long MaxLength = long.MaxValue) {
            if (Stream == null) return false;
            if (Available <= 0 && !await UpdateBuffer()) return false;

            var B = Buffer;
            var S = Position;
            var P = Position;
            var A = Available;
            var L = Length;

            var l = Chain.Length;
            var c = B[P];
            var i = 0;

            try {
                while (c >= 0) {
                    if (Chain[i] == c) {
                        if (++i == l) {
                            await Storage.WriteAsync(B, S, P - S);
                            await Storage.FlushAsync();

                            Position = P;
                            return true;
                        }
                    }
                    else { i = 0; }
                    
                    if (P == Length) {
                        await Storage.WriteAsync(B, S, L - S);
                        await UpdateBuffer();

                        Consume(Available);

                        S = 0;
                        P = Position;
                        L = Length;
                        A = Available;
                    }
                    else {
                        P++;
                    }

                    c = B[P];
                    if (MaxLength-- == 0) return false;
                }
            }
            catch (Exception) { 
                Consume(L - P); 
            }
            return false;
        }

        public async Task<bool> ReceiveUntilConstrained(Stream Storage, byte[] Chain, long MaxLength = long.MaxValue) {
            if (Stream == null) return false;
            if (Available <= 0 && !await UpdateBuffer()) return false;

            var B = Buffer;
            var S = Position;
            var P = Position;
            var A = Available;
            var L = Length;

            try {
                var l = Chain.Length;
                var i = Buffer.FindSubByteArray(Position, Chain, 0, l);

                if (i == -1) {
                    RebaseBuffer();
                    await AppendBuffer();

                    i = Buffer.FindSubByteArray(A, Chain, 0, l);
                    P = Position;
                }

                L = i - P;
                if (i == -1 || L > MaxLength) return false;

                await Storage.WriteAsync(Buffer, Position, L);
                await Storage.FlushAsync();
                Consume(L + Chain.Length);
                return true;
            }
            catch (Exception) { return false; }            
        }

        public Task<string> ReceiveString(int ByteLength) {
            return ReceiveString(ByteLength, Encoding.UTF8);
        }

        public async Task<string> ReceiveString(int ByteLength, Encoding enc) {
            var Storage = new MemoryStream(ByteLength);

            if (await Receive(Storage, ByteLength))
                return enc.GetString(Storage.ToArray());

            return null;
        }

        public async Task<string> ReceiveStringUntil(byte[] chain, Encoding enc) {
            var Out = new MemoryStream();

            if (await ReceiveUntil(Out, chain))
                return enc.GetString(Out.ToArray());

            return null;
        }

        public Task<string> ReceiveStringUntilConstrained(byte[] chain) {
            return ReceiveStringUntilConstrained(chain, Encoding.UTF8);
        }

        public async Task<string> ReceiveStringUntilConstrained(byte[] chain, Encoding enc) {
            var Out = new MemoryStream();

            if (await ReceiveUntilConstrained(Out, chain))
                return enc.GetString(Out.ToArray());

            return null;
        }

		public Task<string> ReceiveLine() {
			return ReceiveLine(Encoding.UTF8);
		}

		public Task<string> ReceiveLine(Encoding enc) {
            return ReceiveStringUntil(NewLineBytes, enc);
		}

		public Task<string> ReceiveLineConstrained() {
			return ReceiveLineConstrained(Encoding.UTF8);
		}

		public Task<string> ReceiveLineConstrained(Encoding enc) {
            return ReceiveStringUntilConstrained(NewLineBytes, enc);
		}
        
        private async Task<bool> UpdateBuffer() {
            try {
                Length = await Stream.ReadAsync(Buffer, 0, Buffer.Length);
                Position = 0;
            }
            catch {
                Position = Length = 0;
            }

            return Length > 0;
        }

        private async Task AppendBuffer() {
            try {
                Length = await Stream.ReadAsync(Buffer, Position, Buffer.Length - Position);
                Position = 0;
            }
            catch { }
        }

        private void RebaseBuffer() {
            if (Available > 0) {
                Array.Copy(Buffer, Position, Buffer, 0, Available);
                Length = Available;
                Position = 0;
            }
        }

        private void ResetBuffer() {
            Position = Length = 0;
            Array.Clear(Buffer, 0, Buffer.Length);
        }

        private void Consume(int len) {
            Position += len;
            TotalBytesConsumed += len;
        }

        private static bool SecureValidationCallback(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors) {
            return errors != SslPolicyErrors.None;
        }
    }
}