using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;


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

        public Server(IPAddress addr, int port, string CertificateFile) : base(addr, port) {
            ServerCert = X509Certificate.CreateFromCertFile(CertificateFile);
            Secure = true;
        }

        public bool AuthClient(Client Client) {
            try {
                Client.Secure = true;
                var Stream = Client.GetStream() as SslStream;

                Stream.AuthenticateAsServer(this.ServerCert, false, SslProtocols.Tls, false);
            }
            catch {
                return false;
            }

            return true;
        }

        public new void Start() {
            base.Start(100);

            BeginAcceptSocket(OnClientConnect, this);
        }

        public new void Stop() {
            base.Stop();
        }

        private void OnClientConnect(IAsyncResult State) {
            try {
                var Socket = this.EndAcceptSocket(State);

                ThreadPool.QueueUserWorkItem(
                    new WaitCallback(OnConnect),
                    Socket
                );
            }
            catch { }

            if (Active)
                BeginAcceptSocket(OnClientConnect, this);
        }

        private void OnConnect(object State) {
            OnConnect(State as Socket);
        }

        public void OnConnect(Socket con) {
            if (this.ClientConnect != null) {
                var Client = (Client)(con);

                if (Secure && !AuthClient(Client))
                    return;

                this.ClientConnect(Client);
            }
        }

        public void OnConnect(Event.Handler Handler) {
            this.ClientConnect = (C) => {
                var Args = new Data.jsObject {
                    { "Client", C }
                };

                Handler(Args);
            };
        }
    }
}
