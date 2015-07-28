using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Net.Irc {
    public partial class Server : Net.Tcp.MultiPortServer {
        public void Broadcast(Packet Packet) {
            Packet.Send(Clients.Cast<Tcp.Client>().ToArray());
        }

        public void Distribute(Connection[] List, Packet Packet) {
            Packet.Send(List);
        }

        public void Distribute(Conversation Conv, Packet Packet, params string[] Ignore) {
            Packet.Send(GetRecipients(Conv, Ignore));
        }

        public void CloseClient(Connection Client) {
            CloseClient(Client, "Connection closed");
        }

        public void CloseClient(Connection Client, string Message) {
            if (Client == null || string.IsNullOrEmpty(Client.Info.Nick))
                return;

            Distribute(GetRecipients(Client.Info), new Packet(Packet.Quit, Client.Info.ToString(), "", Message));            

            foreach (Conversation Conv in Channels.Values) {
                Conv.Users.Remove(Client.Info.Nick);
            }

            Clients.Remove(Client.Info.Nick); 
            Client.Close();
        }

        public bool IsValidLogin(User Info) {
            if (string.IsNullOrEmpty(Info.Nick) || string.IsNullOrEmpty(Info.Ident))
                return false;

            if (Clients.ContainsKey(Info.Nick))
                return false;

            return true;
        }

        public Connection[] GetRecipients(User User) {
            var List = new Dictionary<string, Connection>();

            foreach (var Pair in Channels.ToArray()) {
                var Conv = Pair.Value as Conversation;

                if (Conv.Users.ContainsKey(User.Nick)) {
                    foreach (User Target in Conv.Users.Values.ToArray()) {
                        if (Target == null || Target.Nick == null)
                            continue;

                        if (!List.ContainsKey(Target.Nick)) {
                            List.Add(Target.Nick, Clients[Target.Nick]);
                        }
                    }
                }
            }

            return List.Values.ToArray();
        }

        public Connection[] GetRecipients(Conversation Conv, params string[] Ignore) {
            var List = new Dictionary<string, Connection>();

            foreach (var Nick in Conv.Users.Keys.ToArray()) {
                if (Nick == null || Ignore.Contains(Nick))
                    continue;

                if (!List.ContainsKey(Nick)) {
                    List.Add(Nick, Clients[Nick]);
                }
            }

            return List.Values.ToArray();
        }
    }
}
