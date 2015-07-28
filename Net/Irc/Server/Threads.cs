using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Server : Net.Tcp.MultiPortServer {
        public void HandleInitialConnection(Tcp.Client Client) {
            var Time = DateTime.Now;
            var Connection = new Connection(Client);
            var In = default(Packet);
            var Out = default(Packet);

            while (!IsValidLogin(Connection.Info)) {
                In = Packet.Receive(Connection);

                if (In == null) {
                    if ((DateTime.Now - Time).TotalMilliseconds > PingTimeout) {
                        Connection.Close();
                        return;
                    }
                    continue;
                }

                switch (In.Type) {
                    case Packet.User:
                        var Arguments = In.Args.Split(' ');

                        if (Arguments.Length >= 3) {
                            Connection.Info.Ident = Arguments[0];
                            Connection.Info.Set("Hidden", Arguments[1] == "8");

                            Connection.Info.Realname = In.Message;
                        }
                        break;

                    case Packet.Nick:
                        if (Clients.ContainsKey(In.Args)) {
                            Out = new Packet(Packet.Reply.OnNickInUse, this.Name, "*", "Nick is in use");
                            Out.SetArgs(In.Args);

                            Packet.Send(Connection, Out);
                        }
                        else {
                            Connection.Info.Nick = In.Args;
                        }
                        break;

                    case Packet.Pass:
                        Connection.Info.Password = In.Message;
                        break;
                }
            }

            HandleConnection(Connection);      
        }

        public void HandleConnection(Connection Connection) {
            var Info = Connection.Info;
            var Time = DateTime.Now;

            try {
                Info.Host = Dns.GetHostEntry((Connection.Client.RemoteEndPoint as IPEndPoint).Address).HostName;
            }
            catch {
                Info.Host = Connection.Client.RemoteEndPoint.ToString().MD5();
            }

            Clients.Add(Info.Nick, Connection);
            try {
                Packet.Send(Connection, 
                    Packet.Reply.OnConnected, 
                    this.Name, 
                    Info.Nick, 
                    "Wecome to " + this.Name
                );

                while (Connection.Connected) {
                    var In = Packet.Receive(Connection);
                    var Out = default(Packet);

                    if (In == null) {
                        Thread.Sleep(10);
                        continue;
                    }

                    var Name = string.IsNullOrEmpty(In.Args) ?
                        In.Message :
                        In.Args;

                    var Key = string.IsNullOrEmpty(In.Args) ?
                        In.Args :
                        In.Message;

                    switch (In.Type) {
                        case Packet.Pong:
                            Connection.LastPongTime = DateTime.Now;
                            Info.Set("Ping", (Connection.LastPongTime - Connection.LastPingTime).TotalMilliseconds - PingDelay);
                            break;

                        case Packet.Ping:
                            Packet.Send(Connection, new Packet(Packet.Pong, In.Message));
                            break;

                        case Packet.Msg:
                        case Packet.Notice: {
                                if (this.Channels.ContainsKey(Name)) {
                                    Out = new Packet(In.Type, Info.ToString(), Name, In.Message);

                                    Distribute(Channels[Name], Out, Info.Nick);
                                }
                                else if (this.Clients.ContainsKey(Name)) {
                                    Packet.Send(Clients[Name], In.Type, Info.ToString(), Name, In.Message);
                                }
                                else if (Name.StartsWith("#")) {
                                    Packet.Send(Connection, Packet.Reply.OnNoSuchChannel, Info.ToString(), Name, In.Message);
                                }
                                else {
                                    Packet.Send(Connection, Packet.Reply.OnNoSuchNick, Info.ToString(), Name, In.Message);
                                }
                                break;
                            }

                        case Packet.Join: {
                                if (Channels.ContainsKey(Name)) {
                                    var Chan = Channels[Name];

                                    if (Chan.Key == Key) {
                                        lock (this)
                                        if (!Chan.Users.ContainsKey(Info.Nick))
                                            Chan.Users.Add(Info.Nick, Info);

                                        Out = new Packet(Packet.Join, Info.ToString(), Chan.Name, "");
                                        Packet.Send(Connection, Out);

                                        Out.Set("Sender", Info.ToString());
                                        Distribute(Chan, Out);

                                        if (string.IsNullOrEmpty(Chan.Topic)) {
                                            Packet.Send(Connection, Packet.Reply.OnTopicEmpty, this.Name, Info.Nick, "No topic is set", Chan.Name);
                                        }
                                        else {
                                            Packet.Send(Connection, Packet.Reply.OnTopicEmpty, this.Name, Info.Nick, Chan.Topic, Chan.Name);
                                        }

                                        Packet.Send(Connection, Packet.Reply.OnChannelUserList, this.Name, Info.Nick, string.Join(" ", Chan.Users.Keys.ToArray()), "=", Chan.Name);
                                    }
                                    else {
                                        Packet.Send(Connection, Packet.Reply.OnInvalidPass, this.Name, Chan.Name, "Ah ah ah, didn't say the magic word.");
                                    }
                                }
                                else {
                                    var Chan = new Conversation(Name) {
                                        Key = Key
                                    };

                                    Chan.Users.Add(Info.Nick, Info);
                                    Channels.Add(Chan.Name, Chan);

                                    Packet.Send(Connection, Packet.Join, Info.ToString(), Chan.Name, "");
                                    Packet.Send(Connection, Packet.Reply.OnTopicEmpty, this.Name, Info.Nick, "No topic is set", Chan.Name);
                                    Packet.Send(Connection, Packet.Reply.OnChannelUserList, this.Name, Info.Nick, Info.Nick, "=", Chan.Name);
                                }

                                break;
                            }


                        case Packet.Part:
                            if (Channels.ContainsKey(Name)) {
                                var Chan = Channels[Name];

                                if (Chan.Users.ContainsKey(Info.Nick)) {
                                    Out = new Packet(Packet.Part, Info.ToString(), Chan.Name, In.Message);

                                    Distribute(Chan, Out);

                                    Chan.Users.Remove(Info.Nick);
                                }
                            }
                            break;

                        case Packet.Mode: {
                                if (this.Channels.ContainsKey(Name)) {
                                    Out = new Packet(Packet.Reply.OnModeIs, this.Name, Name, "+" + string.Join("", Channels[Name].Modes.Keys));
                                }
                                else if (this.Clients.ContainsKey(Name)) {
                                    Out = new Packet(Packet.Reply.OnModeIs, this.Name, Name, "+" + string.Join("", Clients[Name].Info.Modes.Keys));
                                }
                                else if (Name.StartsWith("#")) {
                                    Packet.Send(Connection, Packet.Reply.OnNoSuchChannel, Info.ToString(), Name, In.Message);
                                }
                                else {
                                    Packet.Send(Connection, Packet.Reply.OnNoSuchNick, Info.ToString(), Name, In.Message);
                                }

                                Out.Send(Connection);
                                break;
                            }

                        case Packet.Nick:
                            if (Clients.ContainsKey(In.Message)) {
                                Packet.Send(Connection, Packet.Reply.OnNickInUse, this.Name, Info.ToString(), In.Message, "Nick is in use already");
                            }
                            else {
                                var Whom = GetRecipients(Info);

                                Out = new Packet(Packet.Nick, Info.ToString(), "", In.Message);

                                Distribute(Whom, Out);
                                Out.Send(Connection);

                                foreach (var Pair in Channels.ToArray()) {
                                    var Conv = Pair.Value as Conversation;

                                    if (Conv.Users.ContainsKey(Info.Nick)) {
                                        Conv.Users.Remove(Info.Nick);
                                        Conv.Users.Add(In.Message, Info);
                                    }
                                }

                                Clients.Remove(Info.Nick);
                                Info.Nick = In.Message;
                                Clients.Add(Info.Nick, Info);
                            }
                            break;

                        case Packet.Quit:
                            CloseClient(Connection);
                            break;

                        case Packet.Who: {
                                if (this.Channels.ContainsKey(Name)) {
                                    var Chan = this.Channels[Name];

                                    Out = new Packet(Packet.Reply.OnWho, this.Name, Info.Nick, "");

                                    foreach (User User in Chan.Users.Values.ToArray()) {
                                        if (User == null)
                                            continue;

                                        Out.SetArgs(
                                            Chan.Name,
                                            User.Ident,
                                            User.Host,
                                            this.Name,
                                            User.Nick,
                                            "x"
                                        );

                                        Out.Message = string.Format("0 {0}", User.Realname);

                                        Out.Send(Connection);
                                    }

                                    Packet.Send(Connection, Packet.Reply.OnWhoEnd, this.Name, Info.Nick, "End of /WHO list", Chan.Name);
                                }
                                else if (this.Clients.ContainsKey(Name)) {
                                    Packet.Send(Connection, Packet.Reply.OnWhoEnd, this.Name, Info.Nick, "End of /WHO list", Name);
                                }
                                else if (Name.StartsWith("#")) {
                                    Packet.Send(Connection, Packet.Reply.OnNoSuchChannel, Info.ToString(), Name, In.Message);
                                }
                                else {
                                    Packet.Send(Connection, Packet.Reply.OnNoSuchNick, Info.ToString(), Name, In.Message);
                                }
                                break;
                            }

                        case Packet.Topic:
                            if (this.Channels.ContainsKey(Name)) {
                                var Chan = this.Channels[Name];

                                Chan.Topic = In.Message;

                                Out = new Packet(Packet.Topic, Info.ToString(), Chan.Name, Chan.Topic);
                                Distribute(Chan, Out);
                            }
                            break;

                        case Packet.IsOn:
                            Packet.Send(Connection, Packet.IsOn, this.Name, Info.Nick, 
                                string.IsNullOrEmpty(In.Args) ?
                                    "" :
                                    string.Join(" ", 
                                        from u in Clients.Keys
                                            where In.Args.Contains(u)
                                            select u
                                    )
                                );
                            
                            break;
                        
                        default:
                            App.Log.Info(In.Type);
                            break;
                    }
                }
            }
            catch (Exception Error) {
                App.Log.Error(Error.ToString());
            }
                
            CloseClient(Connection);
        }

        public void HandlePinging() {
            while (Active) {
                var List = Clients.ToArray();

                for (int i = 0; i < List.Length; i++) {
                    var Time = DateTime.Now;
                    var Connection = List[i].Value as Connection;

                    if (Connection == null)
                        continue;

                    if (Connection.LastPingTime > Connection.LastPongTime && (Time - Connection.LastPingTime).Milliseconds > PingTimeout)
                        CloseClient(Connection);

                    if ((Time - Connection.LastPingTime).Milliseconds > PingDelay) {
                        Packet.Send(Connection, Packet.Ping, Time.Ticks.ToString());
                        Connection.LastPingTime = Time;
                    }
                }

                Thread.Sleep(50);
            }
        }
    }
}
