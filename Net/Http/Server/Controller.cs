using System;
using System.Collections.Generic;
using System.Reflection;

namespace Poly.Net.Http {
	public class Controller {
        
		public static void RegisterAllHandlers(Server server) {
            foreach (var pair in GetAllHandlers())
                server.On(pair.Route, pair.Handler);
		}

        private static IEnumerable<TypeInfo> GetPublicControllers() {
            var assembly = Assembly.GetEntryAssembly();

            foreach (var type in assembly.DefinedTypes)
                if (typeof(Controller).IsAssignableFrom(type))
                    yield return type;
        }


        private static IEnumerable<(string Route, Server.Handler Handler)> GetControllerHandlers(TypeInfo info) {
            foreach (var method in info.DeclaredMethods) {
                var route = string.Empty;
                var handler = default(Server.Handler);

                if (!method.IsPublic || !method.IsStatic)
                    continue;

                try {
                    handler = (Server.Handler)method.CreateDelegate(typeof(Server.Handler));

					route = StringExtensions.Compare(method.Name, "Index") ?
						$"/{info.Name}/" :
						$"/{info.Name}/{method.Name}/";
                }
                catch { continue; }

                yield return (route, handler);
            }
        }

        private static IEnumerable<(string Route, Server.Handler Handler)> GetAllHandlers() {
            foreach (var controller in GetPublicControllers())
                foreach (var pair in GetControllerHandlers(controller))
                    yield return pair;
        }
    }
}
