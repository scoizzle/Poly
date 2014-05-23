using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Node {
    public class DataType<T> : Node {
        public static object Add(T Left, object Right) { return null; }
        public static object Subtract(T Left, object Right) { return null; }
        public static object Multiply(T Left, object Right) { return null; }
        public static object Devide(T Left, object Right) { return null; }
        public static object Equal(T Left, object Right) { return Object.ReferenceEquals(Left, Right); }
        public static object LessThan(T Left, object Right) { return null; }
        public static object GreaterThan(T Left, object Right) { return null; }
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
            else if (Left is string) {
                return String.Devide((string)Left, Right);
            }
            else if (Left is jsObject) {
                return Object.Devide((jsObject)Left, Right);
            }
            return null;
        }

        public static object Equal(object Left, object Right) {
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

        public static object LessThan(object Left, object Right) {
            if (Left is int) {
                return Integer.LessThan((int)Left, Right);
            }
            else if (Left is double) {
                return Float.LessThan((double)Left, Right);
            }
            return null;
        }

        public static object GreaterThan(object Left, object Right) {
            if (Left is int) {
                return Integer.GreaterThan((int)Left, Right);
            }
            else if (Left is double) {
                return Float.GreaterThan((double)Left, Right);
            }
            return null;
        }
    }
}
