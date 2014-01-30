using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Client : User {
        public bool Connect() {
            return Connect(Server, Port);
        }

        public bool Connect(string Server, int Port) {
            if (Connection == null)
                Connection = new Poly.Net.Tcp.Client();
            else if (Connection.Connected)
                return true;

            Connection.Connect(Server, Port);

            if (Connection.Connected) {
                Connection.autoFlush = true;
                return true;
            }
            return false;
        }

        public void Disconnect() {
            if (Connection == null)
                return;

            Connection.Close();
            Connection = null;
        }

        public void Start() {
            Connect();

            this.ConnectionHandlerThread = new System.Threading.Thread(HandleBasicConnection);
            this.ConnectionHandlerThread.Start();
        }

        public void Stop() {
            Disconnect();

            this.ConnectionHandlerThread.Abort();
            this.ConnectionHandlerThread = null;
            this.Connection = null;
        }

        public void Send(Packet Packet) {
            if (Packet.Message.Contains(Environment.NewLine)) {
                var Messages = Packet.Message.Split(Environment.NewLine);

                foreach (var Msg in Messages) {
                    Packet.Message = Msg;
                    Send(Packet);
                }

                return;
            }
            Packet.Send(Connection);
        }

        public void SendPong(string Message) {
            Send(
                new Packet("Pong") {
                    { "Message", Message }
                }
            );
        }

        public void SendUser(string Name, string RealName, bool Hidden = false) {
            Send(
                new Packet("User") { 
                    { "Ident", Name },
                    { "Visible", Hidden ? "8" : "0" },
                    { "Message", RealName }
                }
            );
        }

        public void SendPass(string Pass) {
            Send(
                new Packet("Pass") {
                    { "Message", Pass }
                }
            );
        }

        public void SendNick(string Nick) {
            Send(
                new Packet("Nick") {
                    { "Message", Nick }
                }
            );
        }

        public void SendOper(string Name, string Pass) {
            Send(
                new Packet("Oper") {
                    { "Sender", Name },
                    { "Message", Pass }
                }
            );
        }

        public void SendTopic(string Convo, string Topic) {
            Send(
                new Packet("Topic") {
                    { "Receiver", Convo },
                    { "Message", Topic }
                }
            );
        }

        public void JoinChannel(string Channel, string Key = "") {
            Send(
                new Packet("Join") {
                    { "Receiver", Channel },
                    { "Message", Key }
                }
            );
            if (!Conversations.ContainsKey(Channel))
                Conversations.Set(Channel, new Conversation(Channel));
        }

        public void PartChannel(string Channel, string Message = "") {
            Send(
                new Packet("Part") {
                    { "Receiver", Channel },
                    { "Message", Message }
                }
            );
            if (Conversations.ContainsKey(Channel))
                Conversations.Remove(Channel);
        }

        public void QuitServer(string Message = "") {
            Send(
                new Packet("Quit") {
                    { "Message", Message }
                }
            );
        }

        public void SendMessage(string Target, string Message) {
            Send(
                new Packet("Msg") {
                    { "Receiver", Target },
                    { "Message", Message }
                }
            );
        }

        public void SendMessage(Conversation Convo, string Message) {
            SendMessage(Convo.Name, Message);
        }

        public void SendMessage(User User, string Message) {
            SendMessage(User.Nick, Message);
        }

        public void SendNotice(string Target, string Message) {
            Send(
                new Packet("Notice") {
                    { "Receiver", Target },
                    { "Message", Message }
                }
            );
        }

        public void SendNotice(Conversation Convo, string Message) {
            SendNotice(Convo.Name, Message);
        }

        public void SendNotice(User User, string Message) {
            SendNotice(User.Nick, Message);
        }

        public void SendCTCP(string Target, string Message) {
            SendMessage(
                Target,
                '\x0001' + Message + '\x0001'
            );
        }

        public void SendCTCPReply(string Target, string Message) {
            SendNotice(
                Target,
                '\x0001' + Message + '\x0001'
            );
        }

        public void SendAction(string Target, string Message) {
            SendCTCP(Target, "ACTION " + Message);
        }

        public void SendAction(Conversation Convo, string Message) {
            SendAction(Convo.Name, Message);
        }

        public void SendAction(User User, string Message) {
            SendAction(User.Nick, Message);
        }

        public void SendWho(string Search) {
            Send(
                new Packet("Who") {
                    { "Receiver", Search }
                }
            );
        }

        public void SendWhois(string Search) {
            Send(
                new Packet("Whois") {
                    { "Receiver", Search }
                }
            );
        }

        public void SendWhowas(string Search) {
            Send(
                new Packet("Whowas") {
                    { "Receiver", Search }
                }
            );
        }

        public void RegisterConnection() {
            if (!Connected)
                return;

            if (!string.IsNullOrEmpty(Password))
                SendPass(Password);

            SendNick(Nick);
            SendUser(Ident, RealName);
        }
    }
}