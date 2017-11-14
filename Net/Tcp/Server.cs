using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;


namespace Poly.Net.Tcp {
    public class Server : TcpListener {
        public int Port { get; private set; }
        public bool Running { get { return Active; } }
        public bool Secure { get; private set; }

        public delegate Task OnClientConnectDelegate(Client Client);
        public event OnClientConnectDelegate OnClientConnected;

        public Task ListenerTask { get; private set; }
        public X509Certificate Certificate { get; private set; }

        public Server(int port) : this(IPAddress.Any, port) { }
        public Server(IPAddress addr, int port) : base(addr, port) { Port = port; }

        public Server(IPAddress addr, int port, X509Certificate cert) : this(addr, port) {
            Certificate = cert;
            Secure = cert != null;
        }

        new public bool Start() {
            if (Active) return true;

            if (OnClientConnected == null) {
                Log.Error("Can't accept connections without OnClientConnect specified!");
                return false;
            }

            try {
                base.Start();

                Log.Info("Now listening on port {0}", Port);
            }
            catch (Exception Error) {
                Log.Error("Couldn't begin accepting connections on port {0}", Port);
                Log.Error(Error);
                return false;
            }

            ListenerTask = StartAcceptTask();
            return true;
        }

        async Task StartAcceptTask() {
            Task lastStarted;
            while (Active) {
                try {
                    var accept_socket = AcceptSocketAsync();
                    var socket = await accept_socket;

                    var client = new Client(socket, Secure);

                    if (Secure) {
                        await client.SecureStream.AuthenticateAsServerAsync(Certificate);
                    }

                    lastStarted = OnClientConnected(client);
                }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
                catch (AuthenticationException) { }
            }
        }
    }
}
