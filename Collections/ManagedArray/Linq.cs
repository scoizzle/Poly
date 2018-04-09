﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Poly.Collections {
    public partial class ManagedArray<T> {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerable<T> All(Func<T, bool> predicate) {
            var elements = this.elements;
            var elements_length = Count;

            for (var i = 0; i < elements.Length && i < elements_length; ++i) {
                var element_current = elements[i];

                if (predicate(element_current))
                    yield return element_current;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForEach(Action<T> action) {
            var elements = this.elements;
            var elements_length = Count;

            for (var i = 0; i < elements.Length && i < elements_length; ++i)
                action(elements[i]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(Func<T, bool> predicate) {
            var elements = this.elements;
            var elements_length = Count;

            for (var i = 0; i < elements.Length && i < elements_length; ++i) {
                var element_current = elements[i];

                if (predicate(element_current))
                    return i;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T SingleOrDefault(Func<T, bool> predicate) {
            var elements = this.elements;
            var elements_length = Count;

            for (var i = 0; i < elements.Length && i < elements_length; ++i) {
                var element_current = elements[i];

                if (predicate(element_current))
                    return element_current;
            }

            return default;
        }
    }
}