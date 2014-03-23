using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class TcpClient : Library {
        public TcpClient() {
            RegisterStaticObject("Tcp", this);

            Add(Client);
            Add(Server);
        }

        public static SystemFunction Client = new SystemFunction("Client", (Args) => {
            return new Net.Tcp.Client();            
        });

        public static SystemFunction Server = new SystemFunction("Server", (Args) => {
            return new Net.Tcp.Server(Args.Get<int>("Port"));
        }, "Port");
    }
}
