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
            } while (Connected);

            App.Log.Warning("Connection lost.");
            App.Log.Warning("Reconnecting.");
            Disconnect();
            Start();
        }

        public void HandlePacket(object objPacket) {
            Packet Packet = objPacket as Packet;

            User Sender = new User(Packet.Sender);
            Sender = Users.Get<User>(Sender.Nick, Sender);

            Conversation Convo = new Conversation(Packet.Receiver);

            if (Packet.Receiver == Nick) {
                if (!Conversations.ContainsKey(Sender.Nick)) {
                    Convo = new Conversation(Sender.Nick);
                    Conversations.Set(Sender.Nick, Convo);
                }
                else {
                    Convo = Conversations.Get<Conversation>(Sender.Nick, Convo);
                }
            }
            else {
                Convo = Conversations.Get<Conversation>(Packet.Receiver, Convo);
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

                    foreach (var N in Nicks) {
                        var Target = N;

                        if (string.IsNullOrEmpty(Target))
                            continue;

                        if (!char.IsLetterOrDigit(Target[0])) {
                            Target = Target.Substring(1);
                        }

                        var Usr = Users.Get<User>(Target, () => { return new User(Target); });

                        if (!char.IsLetter(N[0])) {
                            Usr.Modes[
                                Channel, CharModes.Get<char>(N[0].ToString()).ToString()
                            ] = true;
                        }

                        var Conver = Conversations.Get<Conversation>(Channel, () => { return new Conversation(Channel); });

                        if (!Conver.Users.ContainsKey(Target)) {
                            Conver.Users.Add(Target, Usr);
                        }
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
                    Target.RealName = TargetName;

                    var TargetModes = Packet.Get<string>("Modes").ToCharArray();

                    foreach (var M in TargetModes) {
                        var Mode = M.ToString();

                        if (!char.IsLetter(M)) {
                            if (CharModes.ContainsKey(Mode)) {
                                Target.Modes[TargetChannel, CharModes.Get<string>(Mode)] = true;
                                continue;
                            }
                            break;
                        }

                        Target.Modes[TargetChannel, Mode] = true;
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

                    var Operator = '+';
                    var Channel = Packet.Get<string>("Channel");

                    for (int Index = 0, Offset = 0; Index < Nicks.Length && (Index + Offset) < Modes.Length; ) {
                        var Target = Nicks[Index];
                        var TargetMode = Modes[Index + Offset].ToString();

                        if (TargetMode == "+") {
                            Operator = '+';
                            Offset++;
                            continue;
                        }
                        else if (TargetMode == "-") {
                            Operator = '-';
                            Offset++;
                            continue;
                        }

                        if (Operator == '+') {
                            Conversations[Channel].Modes[TargetMode, Target] = true;
                        }
                        else {
                            Conversations[Channel].Modes[TargetMode, Target] = null;
                        }
                        Index++;
                    }
                } break;

                case "OnMode": {
                    var Modes = Packet.Message.ToCharArray();
                    var Nicks = Packet.Receiver.Split(',');

                    var Operator = '+';

                    for (int Index = 0; Index < Nicks.Length; Index++) {
                        var Target = Nicks[Index];
                        var TargetMode = Modes[Index];

                        if (TargetMode == '+') {
                            Operator = '+';
                            continue;
                        }
                        else if (TargetMode == '-') {
                            Operator = '-';
                            continue;
                        }

                        var User = Users.ContainsKey(Target) ?
                            Users.Get<User>(Target) :
                            Users.Get<User>(Target, new User(Target));

                        if (Operator == '+') {
                            User.Modes[TargetMode.ToString()] = true;
                        }
                        else {
                            User.Modes[TargetMode.ToString()] = null;
                        }
                    }
                } break;
            }
            
            InvokeEvent(Packet.Action, Arguments);
        }
    }
}