using System.Threading.Tasks;

namespace System {
	public static class TaskExtensions {
		public static T AwaitResult<T>(this Task<T> Task) {
			Task.Wait();

			return Task.Result;
		}

		public static T AwaitResult<T>(this Task<T> Task, TimeSpan Duration) {
			Task.Wait(Duration);

			return Task.Result;
		}
	}
}

