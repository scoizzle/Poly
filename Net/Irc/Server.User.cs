﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net.Irc {
    public partial class Server {
        public class User : Irc.User {
            public Net.Tcp.Client Client;
        }
    }
}
