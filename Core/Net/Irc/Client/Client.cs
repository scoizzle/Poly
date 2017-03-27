using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Irc {
    public partial class Client {
        protected Tcp.Client Connection;
        protected KeyValueCollection<ManagedArray<EventHandler>> Events;
        
        public User Info;

        public User ServerUser;
        public Conversation ServerConversation;

        public KeyValueCollection<User> Users;
        public KeyValueCollection<Conversation> Conversations;

        public JSON Config;

        public Client(JSON cfg) {
            Config = cfg;

            Events = new KeyValueCollection<ManagedArray<EventHandler>>();

            Users = new KeyValueCollection<User>();
            Conversations = new KeyValueCollection<Conversation>();

            ServerUser = new User("IRC");
            ServerConversation = new Conversation("Server");
            Info = new User(Config.Get<JSON>("User"));

            On(Packet.Ping, HandlePingPong);
            On(Packet.Reply.OnConnected, HandleAutoJoin);
        }
        
        public bool Connected {
            get {
                return Connection?.Connected == true;
            }
        }
    }
}
