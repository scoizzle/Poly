using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly {
    using Data;

    public partial class Event {
        public class Engine {
			ManagedArray<KeyValuePair<Matcher, Handler>> Handlers;

            public Engine() {
                Handlers = new ManagedArray<KeyValuePair<Matcher, Handler>>();
            }

            public void Add(string Name, Handler Handler) {
                Register(Name, Handler);
            }

            public void Register(string EventName, Handler Handler) {
                Handlers.Add(
                    new KeyValuePair<Matcher, Handler>(
                        new Matcher(EventName), 
                        Handler
                    )
                );
            }

            public void Register(string EventName, Handler Handler, object This) {
                if (This == null) {
                    Register(EventName, Handler);
                    return;
                }

                Register(EventName, (Context) => {
                    Context["this"] = This;
                    return Handler(Context);
                });
            }

            public void Register(string EventName, Handler Handler, string Name, object This) {
                if (string.IsNullOrEmpty(Name) || This == null)
                    return;

                Register(EventName, (Context) => {
                    Context[Name] = This;
                    return Handler(Context);
                });
            }

            public Handler GetHandler(string Data, jsObject Args) {
                var Len = Handlers.Count;
                for (int i = 0; i < Len; i++) {
                    var I = Handlers.Elements[i];
                    var R = I.Key.Match(Data, Args);

                    if (R == null)
                        continue;
                    
                    return I.Value;
                }
                return null;
            }

            public bool MatchAndInvoke(string Data, jsObject Args) {
                var Len = Handlers.Count;
                for (int i = 0; i < Len; i++) {
                    var I = Handlers.Elements[i];
                    var R = I.Key.Match(Data, Args);

                    if (R == null)
                        continue;

                    I.Value(Args);
                    return true;
                }
                return false;
            }
        }
    }
}
