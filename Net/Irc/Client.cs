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

        public bool Connected {
            get {
                return Connection.Connected;
            }
        }

        public jsObject<Conversation> Conversations {
            get {
                if (!ContainsKey("Conversations")) {
                    Set("Conversations", new jsObject<Conversation>() { IsArray = true });
                }
                return Get<jsObject<Conversation>>("Conversations", (jsObject<Conversation>)null);
            }
        }

        public jsObject<User> Users {
            get {
                if (!ContainsKey("Users")) {
                    Set("Users", new jsObject<User>() { IsArray = true });
                }
                return Get<jsObject<User>>("Users", (jsObject<User>)null);
            }
        }

        public jsObject CharModes {
            get {
                if (!ContainsKey("CharModes")) {
                    Set("CharModes", new jsObject() { IsArray = true });
                }
                return getObject("CharModes");
            }
        }

        public string Password {
            get {
                return Get<string>("Password", string.Empty);
            }
            set {
                Set("Password", value);
            }
        }

        public string Realname {
            get {
                return Get<string>("Realname", string.Empty);
            }
            set {
                Set("Realname", value);
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
