using Poly.Data;

namespace Poly.Net.Irc
{
    public partial class Client {
        public delegate void EventHandler(EventContext Context);

        public void Invoke(string EventName, EventContext Context) {
            ManagedArray<EventHandler> Handler;

            if (Events.TryGetValue(EventName, out Handler))
                foreach (var f in Handler)
                    f(Context);
        }

        public void On(string EventName, EventHandler f) {
            ManagedArray<EventHandler> Handler;

            if (Events.TryGetValue(EventName, out Handler)) Handler.Add(f);
            else Events[EventName] = new ManagedArray<EventHandler> { f };
        }

        public void On(Packet.Reply EventId, EventHandler f) {
            On(EventId.ToString(), f);
        }

        private async void HandlePingPong(EventContext Context) {
            await SendPong(Context.Packet.Message);
        }

        private async void HandleAutoJoin(EventContext Context) {
            var Chans = Config.Get<JSON>("Autojoin");

            if (Chans != null)
            foreach (var p in Chans) {
                await JoinChannel(p.Key, p.Value as string);
            }
        }
    }
}