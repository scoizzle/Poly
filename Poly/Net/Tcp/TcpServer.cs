using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Poly.Net.Tcp
{
    public partial class TcpServer {
        readonly IEnumerable<IPEndPoint> requestedEndpoints;
        
        IEnumerable<TcpListener> listeners;

        public TcpServer(int port)
            : this(new IPEndPoint(IPAddress.Any, port)) { }

        public TcpServer(IPAddress address, int port)
            : this(new IPEndPoint(address, port)) { }

        public TcpServer(params IPEndPoint[] endpoints) {
            requestedEndpoints = endpoints;
        }

        public bool Active { get; private set; }

        public Action<TcpClient> OnClientConnect { get; set; }

        public bool Start() {
            if (Active) return true;

            listeners = requestedEndpoints.Select(ep => new TcpListener(ep));
            Active = listeners.All(BeginAcceptSockets);

            if (!Active) {
                Stop();
                return false;
            }

            return true;
        }

        public void Stop() {
            Active = false;

            listeners.All(EndAcceptSockets);
        }

        private bool BeginAcceptSockets(TcpListener tcp) {
            try {
                tcp.Start();
                AcceptSocketsAsync(tcp);
                return true;
            }
            catch (Exception Error) {
                Log.Debug($"Couldn't begin accepting connections on port {tcp.LocalEndpoint}");
                
                Log.Error(Error);
                return false;
            }
        }

        private bool EndAcceptSockets(TcpListener tcp) {
            try {
                Log.Debug($"Shutting down on port {tcp.LocalEndpoint}");
                tcp.Stop();
            }
            catch (Exception error) {
                Log.Error(error);
            }
            return true;
        }

        private async void AcceptSocketsAsync(TcpListener tcp) {
            Log.Debug($"Now accepting connections on port {tcp.LocalEndpoint}");

            do {
                try {
                    var socket = await tcp.AcceptSocketAsync();
                    var client = new TcpClient(socket);

                    OnClientConnect(client);
                }
                catch (Exception error) {
                    Log.Error(error);
                }
            }
            while (Active);
        }
    }
}