using System;
using System.Threading.Tasks;

namespace Poly.Net {
    using Data;
    using Http;

    public partial class HttpServer {
        public interface Module {
            RequestHandler Build(RequestHandler next);
        }

        public class ModuleManager {
            private HttpServer server;
            private ManagedArray<Module> modules;

            internal ModuleManager(HttpServer http_server) {
                server = http_server;
                modules = new ManagedArray<Module>();
                UpdateRequestHandler();
            }

            public void Add(Module module) {
                modules.Add(module);
                UpdateRequestHandler();
            }

            public void Remove(Module module) {
                modules.Remove(module);
                UpdateRequestHandler();
            }

            public T Get<T>() {
                foreach (var mod in modules)
                    if (mod is T result)
                        return result;

                return default;
            }

            private void UpdateRequestHandler() {
                server.handle_request = Build();
            }

            private RequestHandler Build() {
                var execution_chain = new ExecutionChain<RequestHandler>(context => Task.CompletedTask);

                foreach (var mod in modules)
                    execution_chain.Add(mod.Build);

                return execution_chain.Build();
            }
        }        
    }
}

