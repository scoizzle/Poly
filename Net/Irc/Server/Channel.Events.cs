using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net.Irc {
    public partial class Server {
        public partial class Channel : Irc.Conversation {
            public void OnJoin(User User) {
                Users.Add(User.Nick, User);
                User.OnJoin(this);

                var Packet = new Packet("OnJoin") {
                    Sender = User.Nick,
                    Receiver = Name
                };

                Broadcast(Packet);
            }

            public void OnPart(User User) {
                User.OnPart(this);
                Users.Remove(User.Nick);

                var Packet = new Packet("OnPart") {
                    Sender = User.Nick,
                    Receiver = Name
                };

                Broadcast(Packet);
            }

            public void OnDisconnect(User User, string Message) {
                Users.Remove(User.Nick);

                var Packet = new Packet("OnQuit") {
                    Sender = User.Nick,
                    Message = Message
                };

                Broadcast(Packet);
            }

            public void OnMessage(User Sender, Packet Packet) {
                if (!Users.ContainsValue(Sender)) {
                    if (string.IsNullOrEmpty(Password)) {
                        OnJoin(Sender);
                    }
                    else return;
                }

                Broadcast(new Packet("OnMsg") {
                    Sender = Sender.ToString(),
                    Receiver = Packet.Receiver,
                    Message = Packet.Message
                }, Sender.Nick);
            }
        }
    }
}
