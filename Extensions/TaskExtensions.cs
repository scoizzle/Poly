namespace System {
    using Threading;
    using Threading.Tasks;

    static class PolyTaskExtensions {
        public static bool TimeoutAfter(this Task task, TimeSpan timeout) {
            if (task.Wait(timeout))
                return true;

            return false;
        }

        public static TResult TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout) {
            if (task.Wait(timeout))
                return task.Result;

            return default(TResult);
        }

        public static bool TimeoutAfter(this Task task, int timeout) {
            if (task.Wait(timeout))
                return true;

            return false;
        }

        public static TResult TimeoutAfter<TResult>(this Task<TResult> task, int timeout) {
            if (task.Wait(timeout))
                return task.Result;

            return default(TResult);
        }
    }
}
