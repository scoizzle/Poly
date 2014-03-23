using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Irc {
    public partial class Server : MultiPortServer {
        public int PingDelay {
            get {
                if (!Config.ContainsKey("PingDelay"))
                    return 60;
                return Config.Get<int>("PingDelay");
            }
            set {
                Config.Set("PingDelay", value);
            }
        }

        public int PingTimeout {
            get {
                if (!Config.ContainsKey("PingTimeout"))
                    return 10;
                return Config.Get<int>("PingTimeout");
            }
            set {
                Config.Set("PingTimeout", value);
            }
        }

        public string Name {
            get {
                return Config.Get<string>("Name");
            }
            set {
                Config.Set("Name", value);
            }
        }

        public string WelcomeMesage {
            get {
                return Config.Get<string>("Message", "Welcome");
            }
        }
    }
}
