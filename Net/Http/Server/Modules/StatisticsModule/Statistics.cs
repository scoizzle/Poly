using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    public partial class StatisticsModule : HttpServer.Module {
        int  rps, current_rps;
        long bps, current_bps;

        HttpServer server;

        public int RequestsPerSecond { get => rps; }
        public long BytesPerSecond { get => bps; }
        public int ClientsReading { get => server.reading; }
        public int ClientsWriting { get => server.writing; }
        public int ClientsProcessing { get => server.processing; }
        public int ClientsConnected { get => server.active; }

        internal StatisticsModule(HttpServer http_server) {
            server = http_server;

            ResetRps();
            ResetBps();
        }

        public HttpServer.RequestHandler Build(HttpServer.RequestHandler next) =>
            async context => {
                await next(context);

                Interlocked.Increment(ref current_rps);

                var content_length = context.Response.Headers.ContentLength;
                if (content_length.HasValue)
                    Interlocked.Add(ref current_bps, content_length.Value);
            };

        private void ResetRps() {
            rps = Interlocked.Exchange(ref current_rps, 0);
            Task.Delay(1000).ContinueWith(_ => ResetRps());
        }

        private void ResetBps() {
            bps = Interlocked.Exchange(ref current_bps, 0);
            Task.Delay(1000).ContinueWith(_ => ResetBps());
        }
    }

    public static class StatisticsModuleExtensions {
        public static HttpServer EnableStatistics(this HttpServer server) {
            server.Modules.Add(new StatisticsModule(server));
            return server;
        }
    }
}
