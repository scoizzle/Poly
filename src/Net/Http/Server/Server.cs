using System.Threading.Tasks;

namespace Poly.Net {
    public partial class HttpServer {

        public delegate Task RequestHandler(Context context);

        TcpServer tcp_listener;
        RequestHandler handle_request;

        public HttpServer() : this(new Configuration()) { }

        public HttpServer(Configuration config) {
            Config = config;
            Modules = new ModuleManager(this);

            update_rps();
        }

        public bool Running { get => tcp_listener?.Active == true; }

        public Configuration Config { get; set; }

        public ModuleManager Modules { get; private set; }

        public virtual bool Start() {
            if (Config == null)
                return false;

            tcp_listener = new TcpServer(Config.Port);
            tcp_listener.OnAcceptClient += OnClientConnect;

            return tcp_listener.Start();
        }

        public virtual void Stop() {
            tcp_listener?.Stop();
            tcp_listener = null;
        }

        public virtual void Restart() {
            Stop();
            Start();
        }
    }
}