using System;

namespace Poly {

    using Data;

    public partial class Event {

        public class Engine<T> : ManagedArray<(Matcher Matcher, T Handler)> {

            public void On(string event_name, T handler) =>
                Add((new Matcher(event_name), handler));

            public void Add(string event_name, T handler) =>
                Add((new Matcher(event_name), handler));

            public void Remove(string event_name) =>
                RemoveWhere(_ => StringExtensions.Compare(_.Matcher.Format, event_name));

            public bool TryGetHandler(string data, out T handler) {
                if (!string.IsNullOrEmpty(data)) {
                    var items = Elements;
                    var count = Count;

                    for (var i = 0; i < count; i++) {
                        var item = items[i];

                        if (item.Matcher.Compare(data)) {
                            handler = item.Handler;
                            return true;
                        }
                    }
                }

                handler = default;
                return false;
            }

            public bool TryGetHandler(string data, out T handler, out JSON arguments) {
                if (!string.IsNullOrEmpty(data)) {
                    arguments = new JSON();

                    var items = Elements;
                    var count = Count;
                    for (var i = 0; i < count; i++) {
                        var item = items[i];

                        if (item.Matcher.Extract(data, arguments.TrySet)) {
                            handler = item.Handler;
                            return true;
                        }
                    }
                }

                handler = default;
                arguments = null;
                return false;
            }
        }
    }
}