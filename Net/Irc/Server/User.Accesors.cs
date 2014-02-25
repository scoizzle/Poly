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
