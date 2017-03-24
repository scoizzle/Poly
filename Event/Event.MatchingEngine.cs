using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly {
    using Data;

    public partial class Event {
        public class Engine<T> {
			ManagedArray<KeyValuePair<Matcher, T>> Handlers;

            public Engine() {
                Handlers = new ManagedArray<KeyValuePair<Matcher, T>>();
            }

            public void Register(string EventName, T Handler) {
                Handlers.Add(
                    new KeyValuePair<Matcher, T>(
                        new Matcher(EventName), 
                        Handler
                    )
                );
            }

            public T GetHandler(string Data) {
                if (Data == null) return default(T);
                
                var Len = Handlers.Count;
                for (int i = 0; i < Len; i++) {
                    var I = Handlers.Elements[i];

                    if (I.Key.Compare(Data))
                        return I.Value;
                }
                return default(T);
            }

            public T GetHandler(string Data, JSON Args) {
                var Len = Handlers.Count;
                for (int i = 0; i < Len; i++) {
                    var I = Handlers.Elements[i];
                    var R = I.Key.Match(Data, Args);

                    if (R == null)
                        continue;
                    
                    return I.Value;
                }
                return default(T);
            }
        }
    }
}
