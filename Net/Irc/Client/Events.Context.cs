using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Client {
        public class EventContext : JSON {
            public Client Client {
                get { return Get<Client>("Client"); }
                set { Set("Client", value); }
            }

            public Conversation Conv {
                get { return Get<Conversation>("Conv"); }
                set { Set("Conv", value); }
            }

            public User Sender {
                get { return Get<User>("Sender"); }
                set { Set("Sender", value); }
            }
            
            public Packet Packet {
                get { return Get<Packet>("Packet"); }
                set { Set("Packet", value); }
            }
        }
    }
}