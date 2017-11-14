using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly {
    using Data;

    public partial class Event {
        public class Engine<T> : KeyValueCollection<(Matcher Matcher, T Handler)> {
            public void On(string event_name, T handler) {
                Set(event_name, (new Matcher(event_name), handler));
            }

            public bool TryGetHandler(string data, out T handler) {
                if (!string.IsNullOrEmpty(data)) {
                    foreach (var pair in this) {
                        if (pair.Value.Matcher.Compare(data)) {
                            handler = pair.Value.Handler;
                            return true;
                        }
                    }
                }

                handler = default(T);
                return false;
            }

            public bool TryGetHandler(string data, out T handler, out JSON arguments) {
				if (!string.IsNullOrEmpty(data)) {
					arguments = new JSON();

					foreach (var pair in this) {
                        if (pair.Value.Matcher.Extract(data, arguments.TrySet)) {
                            handler = pair.Value.Handler;
							return true;
						}

                        arguments.Clear();
					}
				}

                handler = default(T);
                arguments = null;
                return false;
            }
        }
    }
}
