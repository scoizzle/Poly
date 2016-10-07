using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;


namespace Poly.Net.Tcp {
    public class Server : TcpListener {
        public int Port { get; private set; }
        public bool Running { get { return Active; } }

        public delegate void OnClientConnectDelegate(Client Client);
        public event OnClientConnectDelegate OnClientConnected;

        public Task ListenerTask { get; private set; }

        public Server(int port) : this(IPAddress.Any, port) { }
        public Server(IPAddress addr, int port) : base(addr, port) { Port = port; }

        new public bool Start() {
            if (Active) return true;

            if (OnClientConnected == null) {
                App.Log.Error("Can't accept connections without OnClientConnect specified!");
                return false;
            }

            try {
                base.Start();

                App.Log.Info("Now listening on port {0}", Port);
            }
            catch (Exception Error) {
                App.Log.Error("Couldn't begin accepting connections on port {0}", Port);
                App.Log.Error(Error);
                return false;
            }
            
            ListenerTask = StartAcceptTask();
            return true;
        }

        new public void Stop() {
            Server.Dispose();
            base.Stop();
        }

        private Task StartAcceptTask() {
            return Task.Run(async () => {
                Socket client;

            beginAccept:
                try {
                    while (Active) {
                        client = await AcceptSocketAsync();

                        var Worker = Task.Run(() => {
                            OnClientConnected(client as Socket);
                        });

                        await Task.Delay(0);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception) { goto beginAccept; }
            });
        }
    }
}
