using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Client : User {
        public void HandleBasicConnection() {
            this.RegisterConnection();

            do {
                var Packet = new Packet();

                if (Packet.Receive(Connection)) {
                    ThreadPool.QueueUserWorkItem(
                        new WaitCallback(HandlePacket), 
                        Packet
                    );
                }
                Thread.Sleep(10);
            } while (Connected);

            App.Log.Warning("Connection lost.");
            App.Log.Warning("Reconnecting.");
            Disconnect();
            Start();
        }

        public void HandlePacket(object objPacket) {
            Packet Packet = objPacket as Packet;
            jsObject Info = Packet.Sender.Match(User.TemplateFormat);

            User Sender;

            if (Info != null) {
                Sender = Users.Get<User>(Info.Get<string>("Nick"), () => {
                    return new Irc.User(Info);
                });
            }
            else {
                Sender = new Irc.User(Packet.Sender);
            }

            Conversation Convo;
            if (Packet.Receiver.Contains(' ')) {
                Packet.Receiver = Packet.Receiver.Substring("", " ");
            }

            if (Packet.Receiver == this.Nick) {
                Convo = Conversations.Get<Conversation>(Sender.Nick, () => {
                    return new Conversation(Sender.Nick);
                });
            }
            else {
                Convo = Conversations.Get<Conversation>(Packet.Receiver, () => {
                    return new Conversation(Packet.Receiver);
                });
            }

            jsObject Arguments = new jsObject() {
                { "Convo", Convo },
                { "User", Sender },
                { "Packet", Packet }
            };

            switch (Packet.Action) {
                case "OnPing":
                    SendPong(Packet.Message);
                    break;

                case "OnCTCPPing":
                    SendCTCPReply(Sender.Nick, "PING " + Packet.Message);
                    break;

                case "OnNick":
                    Users.Remove(Sender.Nick);
                    Sender.Nick = Packet.Receiver;
                    Users.Set(Sender.Nick, Sender);
                    break;

                case "OnJoin":
                    if (Sender.Nick != Nick) {
                        Convo.Users.Set(Sender.Nick, Sender);
                    }
                    break;

                case "OnPart":
                    if (Sender.Nick == Nick) {
                        Conversations.Remove(Convo.Name);
                    }
                    else {
                        Convo.Users.Remove(Sender.Nick);
                    }
                    break;

                case "OnQuit":
                    if (Conversations.ContainsKey(Sender.Nick))
                        Conversations.Remove(Sender.Nick);

                    Conversations.ForEach<Conversation>((Name, Conver) => {
                        if (Conver.Users.ContainsKey(Sender.Nick)) {
                            Conver.Users.Remove(Sender.Nick);
                        }
                    });
                    break;

                case "OnTopic":
                case "OnTopicInit":
                    Convo.Topic = Packet.Message;
                    break;

                case "OnChannelUserList":{
                    var Nicks = Packet.Message.Split(' ');
                    var Channel = Packet.Get<string>("Channel");

                    var Conver = Conversations.Get<Conversation>(Channel);

                    if (Conver == null)
                        break;

                    foreach (var N in Nicks) {
                        var Name = N;
                        var C = Name[0];

                        if (!char.IsLetter(C)) {
                            C = CharModes.Get<char>(N.Substring(0, 1));
                            Name = Name.Substring(1);
                        }

                        var Usr = Users.Get<User>(Name, () => { return new User(Name); });

                        if (C != default(char) && !char.IsLetter(N[0])) {
                            Usr.Modes[Channel, C.ToString()] = true;
                        }

                        Conver.Users.Get<User>(Name, Usr);
                    }
                    SendWho(Channel);
                } break;

                case "OnWho": {
                    var TargetNick = Packet.Get<string>("Nick");
                    var TargetIdent = Packet.Get<string>("Ident");
                    var TargetHost = Packet.Get<string>("Host");
                    var TargetName = Packet.Get<string>("RealName");
                    var TargetChannel = Packet.Get<string>("Channel");

                    var Target = Users.ContainsKey(TargetNick) ?
                        Users.Get<User>(TargetNick) :
                        Users.Get<User>(TargetNick, new User(Packet.Template(User.TemplateFormat)));

                    Target.Nick = TargetNick;
                    Target.Ident = TargetIdent;
                    Target.Host = TargetHost;
                    Target.Realname = TargetName;

                    var TargetModes = Packet.Get<string>("Modes").ToCharArray();

                    foreach (var M in TargetModes) {
                        var C = M;
                        if (!char.IsLetter(C))
                            C = CharModes.Get<char>(M.ToString());

                        if (C == default(char))
                            continue;

                        Target.Modes[TargetChannel, C.ToString()] = true;
                    }
                } break;

                case "OnServerFeatures": {
                    var Features = Packet.Get<string>("Features");
                    var Data = Features.Match("*PREFIX=({Modes}){Chars} *");

                    if (Data == null)
                        break;

                    var Modes = Data.Get<string>("Modes").ToCharArray();
                    var Chars = Data.Get<string>("Chars").ToCharArray();

                    for (int Index = 0; Index < Modes.Length; Index++) {
                        CharModes[Chars[Index].ToString()] = Modes[Index];
                    }
                } break;

                case "OnChannelMode": {
                    var Modes = Packet.Message.ToCharArray();
                    var Nicks = Packet.Receiver.Split(',');

                    var Channel = Packet.Get<string>("Channel");
                    
                    var Operator = '+';
                    for (int Index = 0; Index < Nicks.Length && Index < Modes.Length; Index++) {
                        var Target = Nicks[Index];
                        var TargetMode = Modes[Index];

                        if (string.IsNullOrEmpty(TargetMode.ToString()))
                            continue;

                        if (TargetMode == '+') {
                            Operator = '+';
                            continue;
                        }
                        else if (TargetMode == '-') {
                            Operator = '-';
                            continue;
                        }

                        var Conver = Conversations.Get<Conversation>(Channel);

                        if (Conver == null)
                            continue;
                        
                        if (Operator == '+') {
                            Conver.Modes[Target, TargetMode.ToString()] = true;
                        }
                        else {
                            Conver.Modes[Target, TargetMode.ToString()] = null;
                        }
                    }
                } break;

                case "OnMode": {
                    var Modes = Packet.Message.ToCharArray();
                    var Nicks = Packet.Receiver.Split(',');

                    var Operator = '+';

                    for (int Index = 0; Index < Nicks.Length && Index < Modes.Length; Index++) {
                        var Target = Nicks[Index];
                        var TargetMode = Modes[Index];

                        if (string.IsNullOrEmpty(TargetMode.ToString()))
                            continue;

                        if (TargetMode == '+') {
                            Operator = '+';
                            continue;
                        }
                        else if (TargetMode == '-') {
                            Operator = '-';
                            continue;
                        }

                        var Usr = Users.ContainsKey(Target) ?
                            Users.Get<User>(Target) :
                            Users.Get<User>(Target, new User(Target));

                        if (Operator == '+') {
                            Usr.Modes[TargetMode.ToString()] = true;
                        }
                        else {
                            Usr.Modes[TargetMode.ToString()] = null;
                        }
                    }
                } break;
            }
            
            InvokeEvent(Packet.Action, Arguments);
        }
    }
}