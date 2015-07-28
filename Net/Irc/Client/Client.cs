using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Irc {
    public partial class Client : User {
        public Tcp.Client Connection;
        public Event.Engine Events;

        public jsObject CharModes;

        public jsObject<Conversation> Conversations;
        public jsObject<User> Users;


        public Client() {
            Events = new Event.Engine();

            Conversations = new jsObject<Conversation>();
            Users = new jsObject<User>();
            CharModes = new jsObject();
        }
        
        public bool Connected {
            get {
                if (Connection != null)
                    return Connection.Connected;

                return false;
            }
        }

        public string Server {
            get {
                return Get<string>("Server") ?? string.Empty;
            }
            set {
                Set("Server", value);
            }
        }

        public int Port {
            get {
                return Get<int?>("Port") ?? 6667;
            }
            set {
                Set("Port", value);
            }
        }
    }
}
