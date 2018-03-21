using System;

namespace Poly.Collections {
    public class ExecutionChain<T> : ManagedArray<Func<T,T>> {
        public T _default_;

        public ExecutionChain(T default_handler) {
            _default_ = default_handler;
        }

        public T Build() {
            var current = _default_;

            for (var index = Count - 1; index >= 0; --index)
                current = Elements[index](current);

            return current;
        }
    }
}