using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Server {
        public partial class User : Irc.User {
            public bool IsHidden {
                get {
                    return Modes.Get<bool>("i", false);
                }
                set {
                    Modes.Set("i", value);
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
