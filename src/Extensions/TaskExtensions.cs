using System;
using System.Threading.Tasks;
    
namespace Poly {

    public static class TaskExtensions {
        public static bool CatchException(this Task task) {
            if (task.IsFaulted) {
                try { throw task.Exception; }
                catch (Exception error) { Poly.Log.Error(error); }
                return true;
            }
            return false;
        }
    }
}