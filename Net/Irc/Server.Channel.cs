using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net.Irc {
    public partial class Server {
        public class Channel : Irc.Conversation {
            public Channel(string Name)
                : base(Name) {
            }
        }
    }
}
