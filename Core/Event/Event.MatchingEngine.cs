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
				T Handler;
				TryGetHandler(Data, out Handler);
				return Handler;
            }

            public T GetHandler(string Data, JSON Args) {
				T Handler;
				TryGetHandler(Data, Args, out Handler);
				return Handler;
            }

			public bool TryGetHandler(string Data, out T handler) {
				if (!string.IsNullOrEmpty(Data)) {
					var Len = Handlers.Count;
					for (int i = 0; i < Len; i++) {
						var I = Handlers.Elements[i];

						if (I.Key.Compare(Data)) {
							handler = I.Value;
							return true;
						}
					}
				}

				handler = default(T);
				return false;
			}

			public bool TryGetHandler(string Data, JSON Args, out T handler) {
				if (!string.IsNullOrEmpty(Data)) {
					var Len = Handlers.Count;
					for (int i = 0; i < Len; i++) {
						var I = Handlers.Elements[i];
						var R = I.Key.Match(Data, Args);

						if (R != null) {
							handler = I.Value;
							return true;
						}
					}
				}

				handler = default(T);
				return false;
			}
        }
    }
}
