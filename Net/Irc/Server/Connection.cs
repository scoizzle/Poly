using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Server : Net.Tcp.MultiPortServer {
        public class Connection : Tcp.Client {
            public User Info;
            public DateTime LastPingTime, LastPongTime;

            public Connection(Tcp.Client Con) : base(Con) {
                Info = new User();
            }

            public override string ToString() {
                return Info.ToString();
            }
        }
    }
}
