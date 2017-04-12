using System;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Data;

	public partial class Server {
        public delegate void Handler(Request In, Response Out, JSON Args);

        public static Serializer<Server> Serializer = new Serializer<Server>();

		private Tcp.Server Listener;
        private SemaphoreSlim Semaphore;
        private int MaxConcurrentWorkers;
        private Event.Engine<Handler> RequestHandlers;

        private bool Running {
            get {
                return Listener?.Running == true;
            }
        }
        
        public Host Host;

        public Cache Cache;

        public int Port {
            get {
                return Listener?.Port ?? 80;
            }
            set {
                Listener?.Stop();

                Listener = new Tcp.Server(value);
                Listener.OnClientConnected += OnClientConnected;
            }
        }

        public int Concurrency {
            get {
                return MaxConcurrentWorkers;
            }
            set {
                MaxConcurrentWorkers = value;
                Semaphore = new SemaphoreSlim(value);
            }
        }

        public bool UseStaticFiles {
            get {
                return Cache.Active;
            }
            set {
                Cache.Active = value;
            }
        }

        public Server() {
            RequestHandlers = new Event.Engine<Handler>();
            Cache = new Cache(this);
            Concurrency = Environment.ProcessorCount;
        }

        public Server(string hostname, int port) : this() {
            Host = new Host(hostname);
            Port = port;
		}

        ~Server() { 
            Stop();
		}

        public virtual bool Start() {
            Cache.Server = this;
            Cache.Start();
            return Listener?.Start() == true;
        }

        public virtual void Stop() {
            Cache.Stop();
            Listener?.Stop();
            Listener = null;
        }

        public virtual void Restart() {
            Stop();
            Start();
        }

        public void On(string Path, Handler Handler) {
            RequestHandlers.Register(Path, Handler);
        }
    }
}
