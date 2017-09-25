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
        public static readonly byte[] NewLineBytes  = App.NewLineBytes;
        public static int DefaultBufferSize         = 1024 * 16;

        protected MemoryBuffer In, Out;

        public Socket Socket { get; protected set; }
        public Stream Stream { get; protected set; }
        public SslStream SecureStream { get; protected set; }

        public bool Secure { get; private set; }
        public bool AutoFlush { get; set; }

        public IPEndPoint LocalIPEndPoint { get { return Socket?.LocalEndPoint as IPEndPoint; } }
		public IPEndPoint RemoteIPEndPoint { get { return Socket?.RemoteEndPoint as IPEndPoint; } }

        public bool Connected {
            get { return Socket?.Connected == true; }
        }

        public Client() {
        }

        public Client(Socket sock) : this() {
            Socket = sock;
            Stream = new NetworkStream(sock);
            
            Socket.NoDelay = true;

            In = new MemoryBuffer(DefaultBufferSize);
            Out = new MemoryBuffer(DefaultBufferSize);
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

        public async Task<bool> Connect(EndPoint ep) {
            if (Socket == null)
                Socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

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
            In = null;
            Out = null;

            Socket?.Dispose();
        }

        public bool Send() {
            return Out.Write(Stream);
        }
        
        public Task<bool> SendAsync() {
            return Out.WriteAsync(Stream);
        }

        public bool Send(Stream Content) {
            var length = Content.Length;
            while (length > 0) {
                var chunk = Out.Remaining > length ?
                    length :
                    Out.Remaining;

                if (!Out.Read(Content, (int)chunk)) {
                    return false;
                }
                
                length -= chunk;

                if (Out.Remaining == 0 || AutoFlush == true)
                if (!Out.Write(Stream)) {
                    return false;
                }
            }

            return true;
        }

        public async Task<bool> SendAsync(Stream Content) {
            var length = Content.Length;
            while (length > 0) {
                var chunk = Out.Remaining > length ?
                    length :
                    Out.Remaining;

                if (!await Out.ReadAsync(Content, (int)chunk)) {
                    return false;
                }
                
                length -= chunk;

                if (Out.Remaining == 0 || AutoFlush == true)
                if (!await Out.WriteAsync(Stream)) {
                    return false;
                }
            }

            return true;
        }

        public bool Send(byte[] content, int index = 0, int length = -1) {
            if (length == -1)
                length = content.Length;

            while (length > 0) {
                var chunk = Out.Remaining > length ?
                    length :
                    Out.Remaining;

                if (!Out.Read(content, index, chunk)) {
                    return false;
                }

                index += chunk;
                length -= chunk;

                if (Out.Remaining == 0 || AutoFlush == true)
                if (!Out.Write(Stream)) {
                    return false;
                }
            }

            return true;
        }

        public async Task<bool> SendAsync(byte[] content, int index = 0, int length = -1) {
            if (length == -1)
                length = content.Length;

            while (length > 0) {
                var chunk = Out.Remaining > length ?
                    length :
                    Out.Remaining;

                if (!Out.Read(content, index, chunk)) {
                    return false;
                }

                index += chunk;
                length -= chunk;

                if (Out.Remaining == 0 || AutoFlush == true)
                if (!await Out.WriteAsync(Stream)) {
                    return false;
                }
            }

            return true;
        }

        public bool Send(string str) {
            return Send(App.Encoding.GetBytes(str));
        }

		public Task<bool> SendAsync(string str) {
            return SendAsync(App.Encoding.GetBytes(str));
        }

        public bool SendLine(string line) {
            return Send(string.Concat(line, App.NewLine));
        }

        public Task<bool> SendLineAsync(string line) {
            return SendAsync(string.Concat(line, App.NewLine));
        }

        public bool Send(MemoryBuffer buffer) {
            return Send(buffer.Array, buffer.Position, buffer.Available);
        }

        public bool Receive() {
            return In.Read(Stream);
        }

        public Task<bool> ReceiveAsync() {
            return In.ReadAsync(Stream);
        }

        public bool Receive(Stream storage, long length) {
            if (In.Available == 0) {
                var recv = Receive();

                if (!recv)
                    return false;
            }

            do {
                var chunk = In.Available > length ?
                    length :
                    In.Available;

                var write = In.Write(storage, (int)chunk);
                if (!write)
                    return false;

                length -= chunk;

                var recv = Receive();
                if (!recv)
                    return false;
            }
            while (length > 0);

            return true;
        }

        public async Task<bool> ReceiveAsync(Stream storage, long length) {
            if (In.Available == 0) {
                var recv = ReceiveAsync();

                if (!await recv)
                    return false;
            }

            do {
                var chunk = In.Available > length ?
                    length :
                    In.Available;

                var write = In.WriteAsync(storage, (int)chunk);
                if (!await write)
                    return false;

                length -= chunk;

                var recv = ReceiveAsync();
                if (!await recv)
                    return false;
            }
            while (length > 0);

            return true;
        }

        public bool Receive(byte[] bytes, int index = 0, int length = -1) {
            if (length == -1)
                length = bytes.Length;

            if (In.Available == 0) {
                var recv = Receive();

                if (!recv)
                    return false;
            }

            do {
                var chunk = In.Available > length ?
                    length :
                    In.Available;

                var write = In.Write(bytes, index, chunk);
                if (!write)
                    return false;

                length -= chunk;
                index += chunk;

                var recv = Receive();
                if (!recv)
                    return false;
            }
            while (length > 0);

            return true;
        }

        public async Task<bool> ReceiveAsync(byte[] bytes, int index = 0, int length = -1) {
            if (length == -1)
                length = bytes.Length;

            if (In.Available == 0) {
                var recv = ReceiveAsync();

                if (!await recv)
                    return false;
            }

            do {
                var chunk = In.Available > length ?
                    length :
                    In.Available;

                var write = In.Write(bytes, index, chunk);
                if (!write)
                    return false;

                length -= chunk;
                index += chunk;

                var recv = ReceiveAsync();
                if (!await recv)
                    return false;
            }
            while (length > 0);

            return true;
        }

        public bool ReceiveUntil(Stream storage, byte[] chain, long maxLength = long.MaxValue) {
            if (In.Available == 0) {
                var recv = Receive();

                if (!recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;

            var arrayStart = buffer.Position;
            var arrayIndex = arrayStart;
            var arrayLength = buffer.Length;

            var chainIndex = 0;
            var chainLength = chain.Length;

            var lastWasPartial = false;

            do {
                var compare = array.CompareSubByteArray(arrayIndex, chain, chainIndex, chainLength);

                if (compare == true) { // Found chain
                    buffer.Write(storage, arrayIndex - arrayStart);
                    buffer.Consume(chainLength);
                    return true;
                }
                else
                if (compare == false) { // Partial Match
                    if (lastWasPartial) {
                        arrayIndex++;
                    }
                    else {
                        buffer.Write(storage, arrayIndex - arrayStart);
                        buffer.Rebase();

                        Receive();

                        arrayIndex = 0;
                        arrayStart = buffer.Position;
                        arrayLength = buffer.Length;

                        lastWasPartial = true;
                    }
                }
                else 
                if (compare == null) // No match.
                    arrayIndex++;

                if (arrayIndex == arrayLength) {
                    buffer.Write(storage);
                    Receive();

                    arrayIndex = 0;
                    arrayStart = arrayIndex;
                    arrayLength = buffer.Length;
                }
            }
            while (--maxLength > 0);

            return false;
        }

        public async Task<bool> ReceiveUntilAsync(Stream storage, byte[] chain, long maxLength = long.MaxValue) {
            if (In.Available == 0) {
                var recv = ReceiveAsync();

                if (!await recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;

            var arrayStart = buffer.Position;
            var arrayIndex = arrayStart;
            var arrayLength = buffer.Length;

            var chainIndex = 0;
            var chainLength = chain.Length;

            var lastWasPartial = false;

            do {
                var compare = array.CompareSubByteArray(arrayIndex, chain, chainIndex, chainLength);

                if (compare == true) { // Found chain
                    await buffer.WriteAsync(storage, arrayIndex - arrayStart);
                    buffer.Consume(chainLength);
                    return true;
                }
                else
                if (compare == false) { // Partial Match
                    if (lastWasPartial) {
                        arrayIndex++;
                    }
                    else {
                        await buffer.WriteAsync(storage, arrayIndex - arrayStart);
                        buffer.Rebase();

                        await ReceiveAsync();

                        arrayIndex = 0;
                        arrayStart = buffer.Position;
                        arrayLength = buffer.Length;

                        lastWasPartial = true;
                    }
                }
                else 
                if (compare == null) // No match.
                    arrayIndex++;

                if (arrayIndex == arrayLength) {
                    await buffer.WriteAsync(storage);
                    await ReceiveAsync();

                    arrayIndex = 0;
                    arrayStart = arrayIndex;
                    arrayLength = buffer.Length;
                }
            }
            while (--maxLength > 0);

            return false;
        }

        public bool ReceiveUntilConstrained(Stream storage, byte[] chain, int maxLength = int.MaxValue) {
            if (In.Available == 0) {
                var recv = Receive();

                if (!recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;
            var position = buffer.Position;

            var i = array.FindSubByteArray(position, chain);

            if (i == -1 && buffer.Remaining > 0) {
                buffer.Rebase();

                Receive();

                position = buffer.Position;
                i = array.FindSubByteArray(position, chain);
            }

            if (i == -1 || i - position > maxLength)
                return false;
            
            if (!In.Write(storage, i - position))
                return false;

            In.Consume(chain.Length);
            return true;
        }

        public async Task<bool> ReceiveUntilConstrainedAsync(Stream storage, byte[] chain, int maxLength = int.MaxValue) {
            if (In.Available == 0) {
                var recv = ReceiveAsync();

                if (!await recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;
            var position = buffer.Position;

            var i = array.FindSubByteArray(position, chain);

            if (i == -1 && buffer.Remaining > 0) {
                buffer.Rebase();

                await ReceiveAsync();

                position = buffer.Position;
                i = array.FindSubByteArray(position, chain);
            }

            if (i == -1 || i - position > maxLength)
                return false;
            
            if (!await In.WriteAsync(storage, i - position))
                return false;

            In.Consume(chain.Length);
            return true;
        }

        public string ReceiveString(int ByteLength) {
            var Storage = new MemoryStream(ByteLength);

            if (Receive(Storage, ByteLength))
                return App.Encoding.GetString(Storage.ToArray());

            return null;
        }

        public async Task<string> ReceiveStringAsync(int ByteLength) {
            var Storage = new MemoryStream(ByteLength);

            if (await ReceiveAsync(Storage, ByteLength))
                return App.Encoding.GetString(Storage.ToArray());

            return null;
        }

        public string ReceiveStringUntil(byte[] chain) {
            var buffer = new MemoryStream();

            if (ReceiveUntil(buffer, chain))
                return App.Encoding.GetString(buffer.ToArray());

            return null;
        }

        public async Task<string> ReceiveStringUntilAsync(byte[] chain) {
            var buffer = new MemoryStream();

            if (await ReceiveUntilAsync(buffer, chain))
                return App.Encoding.GetString(buffer.ToArray());

            return null;
        }

        public string ReceiveStringUntilConstrained(byte[] chain) {
            if (In.Available == 0) {
                var recv = Receive();

                if (!recv)
                    return null;
            }

            var buffer = In;
            var array = buffer.Array;
            var position = buffer.Position;

            var i = array.FindSubByteArray(position, chain);

            if (i == -1)
                return null;

            var length = i - position;
            var result = App.Encoding.GetString(array, position, length);

            In.Consume(length + chain.Length);
            return result;
        }

        public async Task<string> ReceiveStringUntilConstrainedAsync(byte[] chain) {
            if (In.Available == 0) {
                var recv = ReceiveAsync();

                if (!await recv)
                    return null;
            }

            var buffer = In;
            var array = buffer.Array;
            var position = buffer.Position;

            var i = array.FindSubByteArray(position, chain);

            if (i == -1)
                return null;

            var length = i - position;
            var result = App.Encoding.GetString(array, position, length);

            In.Consume(length + chain.Length);
            return result;
        }

        public string ReceiveLine() {
            return ReceiveStringUntil(NewLineBytes);
        }

		public Task<string> ReceiveLineAsync() {
            return ReceiveStringUntilAsync(NewLineBytes);
		}

		public string ReceiveLineConstrained() {
            return ReceiveStringUntilConstrained(NewLineBytes);
        }

        public Task<string> ReceiveLineConstrainedAsync() {
            return ReceiveStringUntilConstrainedAsync(NewLineBytes);
        }

        private static bool SecureValidationCallback(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors) {
            return errors != SslPolicyErrors.None;
        }
    }
}