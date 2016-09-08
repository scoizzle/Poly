using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;
    using Net.Tcp;
    using Script;

    public partial class Server {
        public Host FindHost(string Query) {
            var Len = Hosts.Count;
            var Elems = Hosts.Elements;

            for (int i = 0; i < Len; i++) {
                var Host = Elems[i];
                if (Host.Matcher.Match(Query) != null)
                    return Host;
            }
            return null;
        }

        private async Task ClientConnected(Client client) {
			try {
                client.ReceiveTimeout = 5000;

				while (client.Connected) {
                    var Request = new Request(client);
                    var Recv = Net.Http.Packet.Receive(client, Request);

                    var Packet = await Recv;
                    if (Packet == null) goto closeConnection;
                    else Request.Prepare();

					var Host = FindHost(Packet.Hostname);
					if (Host == null) goto badRequest;

                    Request.Host = Host;
					OnClientRequest(Request);

                    if (Packet.Connection == "close") goto closeConnection;
                    else continue;

                badRequest:
                    Request.Result = Result.BadRequest;
					Request.Finish();
				}
			}
			catch (Exception Error) {
                App.Log.Error(Error.ToString());
            }

        closeConnection:
            client.Close();
        }
        
        private static void OnClientRequest(Request Request) {
            var Target = Request.Target;
            var Handler = Request.Host.Handlers.GetHandler(Target, Request);

            if (Handler == null) {
                var WWW = Request.Host.GetFullPath(Target);
                var EXT = Request.Host.GetExtension(WWW);

                Request.Set("FileName", WWW);
                Request.Set("FileEXT", EXT);

                Handler = Request.Host.Handlers.GetHandler(EXT, Request);

                if (Handler == null) {
                    Request.SendFile(WWW);
                    return;
                }
            }
            
            Handler(Request);
            Request.Finish();
        }
    }
}
