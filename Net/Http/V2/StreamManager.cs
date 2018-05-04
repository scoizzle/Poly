namespace Poly.Net.Http.V2 {

    public class StreamManager {
        public readonly Settings Settings;
        public readonly Connection Connection;

        //public Dictionary<int, Stream> ActiveStreams;

        //private int NextId;

        //public StreamManager(Connection connection, Settings settings, bool is_client = false) {
        //	ActiveStreams = new Dictionary<int, Stream>();

        //	NextId = is_client ? 1
        //					   : 0;

        // Connection = connection;
        //	Settings = settings;
        //}

        //public Stream Create() {
        //	if (ActiveStreams.Count >= Settings.MaxConcurrentStreams)
        //		return null;

        //	var id = NextId += 2;
        //	var stream = new Stream(this, Settings, id);

        //	ActiveStreams.Add(id, stream);

        //	return stream;
        //}
    }
}