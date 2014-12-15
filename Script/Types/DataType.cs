using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Types {
    using Nodes;
    public class DataType<T> : Value {
        public static object Add(T Left, object Right) { return null; }

        public static object Subtract(T Left, object Right) { return null; }

        public static object Multiply(T Left, object Right) { return null; }

        public static object Devide(T Left, object Right) { return null; }

        public static bool Equal(T Left, object Right) { return Object.ReferenceEquals(Left, Right); }

        public static bool LessThan(T Left, object Right) { return false; }

        public static bool GreaterThan(T Left, object Right) { return false; }
    }

    public class DataType : Node {
        public static object Add(object Left, object Right) {
            if (Left is int) {
                return Integer.Add((int)Left, Right);
            }
            else if (Left is double) {
                return Float.Add((double)Left, Right);
            }
            else if (Left is string) {
                return String.Add((string)Left, Right);
            }
            else if (Left is jsObject) {
                return Object.Add((jsObject)Left, Right);
            }
            return null;
        }

        public static object Subtract(object Left, object Right) {
            if (Left is int) {
                return Integer.Subtract((int)Left, Right);
            }
            else if (Left is double) {
                return Float.Subtract((double)Left, Right);
            }
            else if (Left is string) {
                return String.Subtract((string)Left, Right);
            }
            else if (Left is jsObject) {
                return Object.Subtract((jsObject)Left, Right);
            }
            return null;
        }

        public static object Multiply(object Left, object Right) {
            if (Left is int) {
                return Integer.Multiply((int)Left, Right);
            }
            else if (Left is double) {
                return Float.Multiply((double)Left, Right);
            }
            else if (Left is string) {
                return String.Multiply((string)Left, Right);
            }
            return null;
        }

        public static object Devide(object Left, object Right) {
            if (Left is int) {
                return Integer.Devide((int)Left, Right);
            }
            else if (Left is double) {
                return Float.Devide((double)Left, Right);
            }
            else if (Left is jsObject) {
                return Object.Devide((jsObject)Left, Right);
            }
            return null;
        }

        public static bool Equal(object Left, object Right) {
            if (Object.ReferenceEquals(Left, Right))
                return true;

            if (Left is int) {
                return Integer.Equal((int)Left, Right);
            }
            else if (Left is double) {
                return Float.Equal((double)Left, Right);
            }
            else if (Left is string) {
                return String.Equal((string)Left, Right);
            }
            else if (Left is jsObject) {
                return Object.Equal((jsObject)Left, Right);
            }
            return Right == null;
        }

        public static bool LessThan(object Left, object Right) {
            if (Left is int) {
                return Integer.LessThan((int)Left, Right);
            }
            else if (Left is double) {
                return Float.LessThan((double)Left, Right);
            }
            return false;
        }

        public static bool GreaterThan(object Left, object Right) {
            if (Left is int) {
                return Integer.GreaterThan((int)Left, Right);
            }
            else if (Left is double || Left is long) {
                return Float.GreaterThan(Convert.ToDouble(Left), Right);
            }
            return false;
        }
    }
}
