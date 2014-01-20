using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Event {
    public delegate object Handler(jsObject Args);

    public class Engine : jsObject<Handler> {
        public void Register(string EventName, Handler Handler) {
            this[EventName, Handler.Method.MetadataToken.ToString()] = Handler;
        }

        public void Unregister(string EventName, Handler Handler) {
            this[EventName, Handler.Method.MetadataToken.ToString()] = null;
        }

        public void Invoke(string EventName, jsObject Args) {
            var List = getObject(EventName);

            if (List != null) {
                List.ForEach((K, V) => {
                    (V as Handler)(Args);
                });
            }
        }

        public bool MatchAndInvoke(string Data, jsObject Args) {
            return MatchAndInvoke(Data, Args, false);
        }

        public bool MatchAndInvoke(string Data, jsObject Args, bool KeyIsWild) {
            foreach (var Pair in this) {
                var Key = KeyIsWild ? Data : Pair.Key;
                var Wild = KeyIsWild ? Pair.Key : Data;

                var Matches = Key.Match(Wild);

                if (Matches != null) {
                    Args.CopyTo(Matches);
                    Invoke(Pair.Key, Matches);
                    return true;
                }
            }
            return false;
        }
    }
}
