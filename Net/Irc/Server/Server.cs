using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Server : Tcp.MultiPortServer {
        public jsObject<Conversation> Channels;
        public jsObject<Connection> Clients;
        public int PingDelay, PingTimeout;

        public string Name;

        public Server() {
            Channels = new jsObject<Conversation>();
            Clients = new jsObject<Connection>();

            PingDelay = 10000;
            PingTimeout = 10000;

            Name = string.Empty;

            OnStart += () => {
                Task.Factory.StartNew(HandlePinging, TaskCreationOptions.LongRunning);
            };

            OnClientConnect += (Client) => {
                HandleInitialConnection(Client);
            };
        }
    }
}
