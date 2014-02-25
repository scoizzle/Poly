using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Net.Irc {
    public partial class Server {
        public override void OnClientConnect(Tcp.Client Client) {
            var User = new User(this, Client) {
                Host = Client.Client.RemoteEndPoint.ToString().MD5()
            };

            while (Client.Connected) {
                var Packet = new Packet();

                if (Packet.Receive(Client)) {
                    HandlePacket(User, Packet);
                }
            }

            User.OnDisconnect("Client closed.");
        }

        public void HandlePacket(User User, Packet Packet) {
            App.Log.Info("Event: " + Packet.Action);
            switch (Packet.Action) {
                case "Pong":
                    User.OnPong(Packet.Message);
                    break;

                case "User":
                    if (!User.IsAuthenticated) {
                        User.Nick = Packet.getString("Ident");
                        User.Ident = Packet.getString("Ident");
                        User.IsHidden = (User.getInt("Visible") == 8);
                        User.RealName = Packet.Message;

                        User.Ping();

                        if (!User.IsAuthenticated) {
                            for (int i = 0; i < PingTimeout * 1000; i++) {
                                if (User.LastPongTime != DateTime.MinValue) {
                                    OnUserAuthenticated(User);
                                    return;
                                }
                                Thread.Sleep(1);
                            }

                            User.Disconnect("Ping timeout.");
                            return;
                        }
                    }
                    break;

                case "Pass":
                    if (!User.IsAuthenticated) {
                        User.Password = Packet.Message;
                    }
                    break;

                case "Nick":
                    if (Users.Search<User>(Packet.Message) != null) {
                        User.Send(new Packet("OnNickInUse") {
                            
                        });
                    }
                    else {
                        User.OnNick(Packet.Message);
                    }
                    break;

                case "Join": {
                    var Channel = Channels.Search<Channel>(Packet.Receiver);

                    if (Channel == null) {
                        Channel = new Channel(Packet.Receiver);

                        if (!string.IsNullOrEmpty(Packet.Message)) {
                            Channel.Password = Packet.Message;
                        }

                        Channel.OnJoin(User);

                        Channels.Add(Channel.Name, Channel);
                    }
                    else if (Channel.Password == Packet.Message) {
                        Channel.OnJoin(User);
                    }
                    else {
                        User.Send(new Packet("OnBadChannelKey") {
                            Sender = Name,
                            Receiver = User.ToString(),
                            Message = "Incorrect channel key."
                        });
                    }
                    break;
                }

                case "Part": {
                    var Channel = Channels.Search<Channel>(Packet.Receiver);

                    if (Channel != null) {
                        Channel.OnPart(User);
                    }
                    break;
                }

                case "Msg":
                case "Notice": {
                    var Chan = Channels.Search<Channel>(Packet.Receiver);
                    if (Chan != null) {
                        Chan.OnMessage(User, Packet);
                        return;
                    }

                    var Usr = Users.Search<User>(Packet.Receiver);
                    if (Usr != null) {
                        Usr.OnMessage(User, Packet);
                        return;
                    }

                    User.Send(new Packet("OnNoSuchNick") {
                        Sender = Name,
                        Receiver = User.ToString(),
                        Message = "No such nick/channel."
                    });
                    break;
                }
            }
        }

        public void OnUserAuthenticated(User User) {
            User.IsAuthenticated = true;
            Users.Add(User.Nick, User);

            User.Send(new Packet("Notice") {
                Sender = Name,
                Receiver = "Auth",
                Message = WelcomeMesage
            });
        }
    }
}
