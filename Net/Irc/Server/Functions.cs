using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net.Irc {
    public partial class Server {
        public void SendPacket(Packet Packet, params User[] Users) {
            for (int i = 0; i < Users.Length; i++) {
                if (Users[i] != null) {
                    Packet.Send(Users[i].Client);
                }
            }
        }

        public void SendPacketToChannelUsers(Packet Packet, Channel Channel) {
            Channel.Users.ForEach((K, V) => {
                SendPacket(Packet, V as User);
            });
        }

        public IEnumerable<Channel> FindUserChannels(User User) {
            var List = Channels.ToList();

            for (int i = 0; i < List.Count; i++) {
                var Channel = List[i].Value as Channel;

                if (Channel.Users.ContainsKey(User.Nick))
                    yield return Channel;
            }
        }

        public void SendPing(User User) {
            User.LastPingMessage = DateTime.Now.GetHashCode().ToString();

            SendPacket(
                new Packet("OnPing") {
                    Message = User.LastPingMessage
                }, 
                User
            );
        }

        public void DisconnectUser(User User, string Message) {
            var Packet = new Packet("OnQuit") {
                Sender = User.Nick,
                Message = Message
            };

            SendPacket(Packet, User);

            foreach (var Channel in FindUserChannels(User)) {
                Channel.Users.Remove(User.Nick);

                SendPacketToChannelUsers(Packet, Channel);
            }

            User.Client.Close();
        }
    }
}