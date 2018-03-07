namespace System {
    using System.Threading.Tasks;

    public static class TaskExtensions {
        public static Task TimeoutAfter(this Task task, TimeSpan time, Action callback) {
            Task.Delay(time).ContinueWith(_ => {
                if (!task.IsCompleted)
                    callback();
            });

            return task;
        }

        public static Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan time, Action callback) {
            Task.Delay(time).ContinueWith(_ => {
                if (!task.IsCompleted)
                    callback();
            });

            return task;
        }

        public static bool CatchException(this Task task) {
            if (task.IsFaulted) {
                try { throw task.Exception; }
                catch (Exception error) {
                    Poly.Log.Error(error);
                }
                return true;
            }
            return false;
        }

        public static bool CatchException<T>(this Task<T> task) {
            if (task.IsFaulted) {
                try { throw task.Exception; }
                catch (Exception error) {
                    Poly.Log.Error(error);
                }
                return true;
            }
            return false;
        }
    }
}