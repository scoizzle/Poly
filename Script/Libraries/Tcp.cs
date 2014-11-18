using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    public class TcpClient : Library {
        public TcpClient() {
            RegisterStaticObject("Tcp", this);

            Add(Client);
            Add(Server);
        }

        public static Function Client = new Function("Client", (Args) => {
            return new Net.Tcp.Client();            
        });

        public static Function Server = new Function("Server", (Args) => {
            return new Net.Tcp.Server(Args.Get<int>("Port"));
        }, "Port");
    }
}
