using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Threading.Tasks {
    public static class TaskExtensions {
        public static void Execute(this Task Task) {
            Task.Wait();
        }

        public static T Execute<T>(this Task<T> Task) {
            Task.Wait();
            return Task.Result;
        }
    }
}
