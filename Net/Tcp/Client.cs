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

    public class Client : MemoryBufferedStream {
        public bool Secure { get; private set; }

        public Socket Socket { get; protected set; }
        public SslStream SecureStream { get; protected set; }

        public IPEndPoint LocalIPEndPoint { get { return Socket?.LocalEndPoint as IPEndPoint; } }
		public IPEndPoint RemoteIPEndPoint { get { return Socket?.RemoteEndPoint as IPEndPoint; } }

        public bool Connected {
            get { return Socket?.Connected == true; }
        }

        public Client(Socket sock) : base(new NetworkStream(sock)){
            Socket = sock;
            Socket.NoDelay = true;
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

        static bool SecureValidationCallback(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors) {
            return errors != SslPolicyErrors.None;
        }
    }
}