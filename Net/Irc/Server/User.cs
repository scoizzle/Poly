using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net.Irc {
    public partial class Server {
        public class User : Irc.User {
            public Net.Tcp.Client Client;
            public DateTime LastPongTime = DateTime.MinValue;
            public string LastPingMessage = "";

            public bool IsHidden {
                get {
                    return Get<bool>("IsHidden", false);
                }
                set {
                    Set("IsHidden", value);
                }
            }

            public bool IsAuthenticated {
                get {
                    return Get<bool>("IsAuthenticated", false);
                }
                set {
                    Set("IsAuthenticated", value);
                }
            }
        }
    }
}
