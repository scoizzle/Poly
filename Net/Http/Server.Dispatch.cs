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

        private void ClientConnected(Client client) {
			try {
                client.ReceiveTimeout = 5000;

				while (client.Connected) {
                    var request = new Request(client);

					if (!Packet.Receive(client, request)) goto closeConnection;
					else request.Prepare();

					var host = FindHost(request.Hostname);
					if (host == null) goto badRequest;

                    request.Host = host;
					OnClientRequest(request);

                    if (request.Connection == "close") goto closeConnection;
                    else continue;

                badRequest:
                    request.Result = Result.BadRequest;
					request.Finish();
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
