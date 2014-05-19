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
        public Poly.Net.Tcp.Client Connection = new Poly.Net.Tcp.Client();
        private Thread ConnectionHandlerThread = null;

        public Client() {

        }
        
        public bool Connected {
            get {
                if (Connection == null)
                    return false;

                return Connection.Connected;
            }
        }

        public jsObject<Conversation> Conversations {
            get {
                return Get<jsObject<Conversation>>("Conversations", jsObject<Conversation>.NewTypedObject);
            }
        }

        public jsObject<User> Users {
            get {
                return Get<jsObject<User>>("Users", jsObject<User>.NewTypedObject);
            }
        }

        public jsObject CharModes {
            get {
                return Get<jsObject>("CharModes", () => { 
                    return new jsObject(
                        "@", "o",
                        "+", "v"
                    ); 
                });
            }
        }

        public string Server {
            get {
                return Get<string>("Server", string.Empty);
            }
            set {
                Set("Server", value);
            }
        }

        public int Port {
            get {
                return Get<int>("Port", 6667);
            }
            set {
                Set("Port", value);
            }
        }
    }
}
