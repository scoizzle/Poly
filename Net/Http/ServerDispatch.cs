using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private void OnClientConnected(Client client) {
            try {
                var Stream = client.GetStreamer();

				while (Running && client.Connected && !Cancel.IsCancellationRequested) {
                    if (Stream.Available == 0) {
                        Thread.Sleep(0);
                        Stream.UpdateBuffer();
                        continue;
                    }

                    var request = new Request(client);

                    if (!Packet.Receive(client, request))
                        break;

                    if (!Matcher.Compare(request.Hostname)) {
                        request.Result = Result.BadRequest;
                        request.Result.Headers.Set("Connection", "close");
                        request.Finish();
                        goto closeConnection;
                    }
                    else {
                        request.Prepare();
                        request.Host = this;
                    }
                    
                    OnClientRequest(request);
                }
			}
			catch (Exception Error) {
                App.Log.Error(Error.ToString());
            }

        closeConnection:
            client.Dispose();
        }
        
        private void OnClientRequest(Request Request) {
            var Target = Request.Target;
            var EXT = Target.GetFileExtension();
            
            if (EXT.Length != 0 && Handlers.MatchAndInvoke(EXT, Request))
                goto finish;

            var Cached = Cache.Get(Target);
            if (Cached != null) {
                Request.SendFile(Cached);
                goto finish;
            }

            Request.ProcessHeaders();
            if (Handlers.MatchAndInvoke(Target, Request))
                goto finish;

            Request.Result.Status = Result.BadRequest;

        finish:
            Request.Finish();
        }
    }
}
