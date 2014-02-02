using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Irc {
    public partial class Server : MultiPortServer {
        public jsObject<User> Users = new jsObject<User>();
        public jsObject<Channel> Channels = new jsObject<Channel>();


        public int PingTimeout = 60;
    }
}
