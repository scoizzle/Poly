using System.Collections.Generic;
using System.Threading.Tasks;

namespace Poly.Net {
    using Collections;

    public partial class HttpServer {
        public interface Module {
            RequestHandler Build(RequestHandler next);
        }

        public class ModuleManager {
            private HttpServer server;
            private List<Module> modules;

            internal ModuleManager(HttpServer http_server) {
                server = http_server;
                modules = new List<Module>();
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
                var execution_chain = new ExecutionChain<RequestHandler>(context => Task.CompletedTask);

                foreach (var mod in modules)
                    execution_chain.Add(mod.Build);

                server.handle_request = execution_chain.Build();
            }
        }        
    }
}

