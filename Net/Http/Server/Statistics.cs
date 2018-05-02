using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Poly.Net {
    using Http;

    public partial class HttpServer {
        private int connected, reading, writing, processing;
        private int rps, rps_current;

        public int Connected => connected;
        public int Receiving => reading;
        public int Processing => processing;
        public int Sending => writing;

        public int RequestsPerSecond => rps;

        private void client_connected() =>
            Interlocked.Increment(ref connected);

        private void client_disconnected() =>
            Interlocked.Decrement(ref connected);

        private Stopwatch client_began_reading(Context _) {
            Interlocked.Increment(ref reading);
            return _.Timer.Start("ReceiveRequest");
        }

        private void client_ended_reading(Context _) {
            Interlocked.Decrement(ref reading);
            _.Timer.Stop("ReceiveRequest");
        }
        
        private Stopwatch client_began_processing(Context _) {
            Interlocked.Increment(ref processing);
            return _.Timer.Start("ProcessRequest");
        }

        private void client_ended_processing(Context _) {
            Interlocked.Decrement(ref processing);
            _.Timer.Stop("ProcessRequest");
        }

        private Stopwatch client_began_writing(Context _) {
            Interlocked.Increment(ref writing);
            return _.Timer.Start("SendResponse");
        }

        private void client_ended_writing(Context _) {
            Interlocked.Decrement(ref writing);
            _.Timer.Stop("SendResponse");
        }

        private void request_completed(Context context) {
            Interlocked.Increment(ref rps_current);

            context.Reset();
        }

        private void update_rps() {
            rps = Interlocked.Exchange(ref rps_current, 0);
            Task.Delay(1000).ContinueWith(_ => update_rps());
        }
    }
}