using System.Linq;
using System.Threading.Tasks;
using Poly.Data;

namespace Poly.Net.Irc
{
    public partial class Packet : JSON {
        public static Matcher Fmt = new Matcher(@":{Sender} {Type} ({Receiver} )?({Args:![:]} )?:{Message}?"),
                              HlFmt = new Matcher(@"{Type:!Whitespace}( {Args:![:]})?( :{Message})?");

        public bool IsHeadless { get; private set; } = true;

        public string Type { 
            get { return Get<string>("Type"); }
            set { Set("Type", value); }
        }

        public string Message { 
            get { return Get<string>("Message"); }
            set { Set("Message", value); }
        }

        public string Sender { 
            get { return Get<string>("Sender"); }
            set { Set("Sender", value); }
        }

        public string Receiver { 
            get { return Get<string>("Receiver"); }
            set { Set("Receiver", value); }
        }
        
        public string[] Arguments { 
            get { return Get<string>("Args")?.Split(' '); }
            set { Set("Args", string.Join(" ", value)); }
        }

        public bool IsCTCP {
            get {
                if (IsHeadless) return false;
                return Message?.FirstOrDefault() == '\x01' && Message?.LastOrDefault() == '\x01';
            }
            set
            {
                var msg = Message;

                if (value) {
                    if (!IsCTCP)
                        Message = '\x01' + msg + '\x01';
                }
                else
                if (IsCTCP)
                    Message = msg.Substring(1, msg.Length - 2);
            }
        }

        public Packet() { }

        public Packet(string type, string sender = null, string receiver = null, string msg = null, params string[] args) {
            Type = type;
            Sender = sender;
            Receiver = receiver;
            Message = msg;
            Arguments = args;
        }

        public Packet(Packet.Reply type, string sender = null, string receiver = null, string msg = null, params string[] args){
            Type = ((int)type).ToString("D3");
            Sender = sender;
            Receiver = receiver;
            Message = msg;
            Arguments = args;
        }

        public Task<bool> Send(Tcp.Client Client) {
            var Debug = ToString();
            return Client?.SendLine(Debug);
        }

        public Task<bool[]> Send(Tcp.Client[] List) {
            var rawPacket = ToString();

            return Task.WhenAll(
                List.Select(c => c?.Send(rawPacket))
            );
        }

        public async Task<bool> Receive(Tcp.Client Client) {
            var Line = await Client.ReceiveLineConstrained();

            if (string.IsNullOrEmpty(Line)) return false;

            if (Line.StartsWith(":")) {
                IsHeadless = false;
                return Fmt.Match(Line, this) != null;
            }
            else {
                IsHeadless = true;
                return HlFmt.Match(Line, this) != null;
            }
        }
        
        public override string ToString() {
            if (IsHeadless) {
                return HlFmt.Template(this);
            }
            else {
                return Fmt.Template(this);
            }
        }
    }
}