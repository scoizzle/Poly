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

		public IEnumerable<object> Execute(string EventName, jsObject Args) {
			var List = getObject (EventName);

			for (int i = 0; i < List.Count; i++) {
				yield return (List.ElementAt(i).Value as Handler)(Args);
			}
		}

        public bool MatchAndInvoke(string Data, jsObject Args) {
            return MatchAndInvoke(Data, Args, false);
        }

        public bool MatchAndInvoke(string Data, jsObject Args, bool KeyIsWild) {
			ForEach ((K, V) => {
				var Key = KeyIsWild ? Data : K;
				var Wild = KeyIsWild ? K : Data;

				var Matches = Key.Match (Wild);

				if (Matches != null) {
					Args.CopyTo (Matches);
					Invoke (K, Matches);
				}
			});
			return true;
        }
    }
}
