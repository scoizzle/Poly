using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Server {
        public partial class User : Irc.User {
            public void OnPong(string Message) {
                if (LastPingMessage != Message) {
                    Disconnect("Ping timeout.");
                    return;
                }
                LastPongTime = DateTime.Now;
            }

            public jsObject<User> FindAllRecipients() {
                var List = new jsObject<User>();

                Channels.ForEach<Channel>((Chan, Channel) => {
                    Channel.Users.ForEach<User>((Usr, User) => {
                        if (!List.ContainsKey(Usr) && Usr != this.Nick) {
                            List.Add(Usr, User);
                        }
                    });
                });

                return List;
            }

            public void OnNick(string Nick) {
                var List = FindAllRecipients();

                Channels.ForEach<Channel>((Chan, Channel) => {
                    Channel.Users.Remove(this.Nick);
                    Channel.Users.Add(Nick, this);
                });

                var Packet = new Packet("OnNick") {
                    Sender = this.ToString(),
                    Receiver = Nick
                };

                if (List.Count > 0) {
                    List.ForEach<User>((Usr, User) => {
                        User.Send(Packet);
                    });
                }

                this.Nick = Nick;
            }

            public void OnJoin(Channel Channel) {
                if (!this.Channels.ContainsKey(Channel.Name)) {
                    this.Channels.Add(Channel.Name, Channel);
                }
                
                Send(new Packet("OnJoin") {
                    Sender = ToString(),
                    Receiver = Channel.Name
                });

                Send(
                    new Packet("OnChannelUserList") {
                        { "Sender", Server.Name },
                        { "Receiver", ToString() },
                        { "Channel", Channel.Name },
                        { "Message", string.Join(" ", Channel.Users.Keys) }
                    },
                    new Packet("OnNAMESEnd") {
                        Sender = Server.Name,
                        Receiver = Nick,
                        Message = Server.Config.Get<string>("Message", "NameListEnd")
                    }
                );
            }

            public void OnPart(Channel Channel) {
                this.Channels.Remove(Channel.Name);
            }

            public void OnMessage(User Sender, Packet Packet) {
                Send(new Packet("OnMsg") {
                    Sender = Sender.ToString(),
                    Receiver = Packet.Receiver,
                    Message = Packet.Message
                });
            }

            public void OnDisconnect(string Message) {
                Channels.ForEach<Channel>((Key, Chan) => {
                    Chan.OnDisconnect(this, Message);
                });
            }
        }
    }
}
