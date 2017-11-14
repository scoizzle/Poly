using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;
	using Tcp;

	public partial class Server {
        async Task OnConnected(Client client) {
			Connection connection = new V1.Connection(client);

			try {
				while (Running && client.Connected) {
					var receive = connection.Receive(out Request request);
					if (!receive)
						break;
                    
                    var response = ProcessRequest(request);
                    if (response == null)
                        connection.New(out response, Result.InternalError);

					var send = connection.Send(response);
					if (!send)
						break;

					await Task.Yield();
				}
			}
            catch (SocketException) { }
            catch (IOException) { }
			catch (Exception error) {
				Log.Debug(error);
			}

			client.Close();
		}

        Response VerifyHost(Request request) {
			if (Config.Host.Matcher.Compare(request.Authority))
                return HandleRequest(request);

            request.Connection.New(out Response response, Result.BadRequest);
            return response;
		}

		Response HandleRequest(Request request) {
			var handler_found =
                Handlers.TryGetHandler(
                    request.Target,
					out Handler handler,
                    out JSON arguments);

            if (handler_found) {
                request.Arguments = arguments;
                return handler(request);
			}

            request.Connection.New(out Response response, Result.NotFound);
			return response;
		}
	}
}