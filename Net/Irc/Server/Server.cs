using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Irc {
    public partial class Server : MultiPortServer {
        public jsObject Config = new jsObject();

        public jsObject<User> Users = new jsObject<User>();
        public jsObject<Channel> Channels = new jsObject<Channel>();

        public Server() {
        }

        public void Configure(jsObject Config) {
            Config.CopyTo(this.Config);
        }

        public new void Start() {
            ThreadPool.QueueUserWorkItem(new WaitCallback(
                PingPong
            ));

            base.Start();
        }
    }
}
