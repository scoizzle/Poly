using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;


namespace Poly.Net.Tcp {
    public class Server : TcpListener {
        public int Port = int.MaxValue;

        public readonly bool Secure = false;
        public readonly X509Certificate ServerCert = null;

        public delegate void ClientConnectHandler(Client Client);
        public event ClientConnectHandler ClientConnect;

        public new bool Active {
            get {
                return base.Active;
            }
        }

        public Server(int port)
            : this(IPAddress.Any, port) {
            this.Port = port;
        }

        public Server(IPAddress addr, int port)
            : base(addr, port) {
            this.Port = port;
        }

        public Server(IPAddress addr, int port, string CertificateFile)
            : base(addr, port) {
            ServerCert = X509Certificate.CreateFromCertFile(CertificateFile);
            Secure = true;
        }

        public bool AuthClient(Client Client) {
            try {
                Client.Secure = true;
                var Stream = Client.Stream as SslStream;

                Stream.AuthenticateAsServer(this.ServerCert, false, SslProtocols.Tls, false);

                return true;
            }
            catch { }
            return false;
        }

        new public async void Start() {
            if (ClientConnect == null)
                throw new NullReferenceException("Must specify ClientConnect handler!");

            base.Start(10240);
            await AcceptConnections();
        }

        public new void Stop() {
            base.Stop();
        }

        private async Task AcceptConnections() {
            while (Active) {
                try {
                    var Sock = AcceptSocket();

                    await Task.Run(() => {
                        var Client = new Client(Sock);

                        if (Secure)
                            if (!AuthClient(Client))
                                return;

                        ClientConnect(Client);
                    });
                }
                catch { }
            }
        }
    }
}
