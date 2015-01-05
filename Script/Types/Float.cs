using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Types {
    using Nodes;

    public class Float : DataType<double> {
        public double Value;

        public Float(double Val) {
            this.Value = Val;
        }

        public override object Evaluate(Data.jsObject Context) {
            return Value;
        }

        public override string ToString() {
            if (Value != null)
                return Value.ToString();

            return string.Empty;
        }

        public new static object Add(double Left, object Right) {
            if (Right is int) {
                return Left + (int)Right;
            }
            else if (Right is double) {
                return Left + (double)Right;
            }
            else if (Right is string) {
                var Str = (string)Right;
                var Temp = double.NaN;

                if (double.TryParse(Str, out Temp))
                    return Left + Temp;

                return Left + (string)Right;
            }
            return null;
        }

        public new static object Subtract(double Left, object Right) {
            if (Right is int) {
                return Left - (int)Right;
            }
            else if (Right is double) {
                return Left - (double)Right;
            }
            else if (Right is string) {
                var Str = (string)Right;
                var Temp = double.NaN;

                if (double.TryParse(Str, out Temp))
                    return Left - Temp;
            }
            return null;
        }

        public new static object Multiply(double Left, object Right) {
            if (Right is int) {
                return Left * (int)Right;
            }
            else if (Right is double) {
                return Left * (double)Right;
            }
            else if (Right is string) {
                var Str = (string)Right;
                var Temp = double.NaN;

                if (double.TryParse(Str, out Temp))
                    return Left * Temp;
            }
            return null;
        }

        public new static object Devide(double Left, object Right) {
            if (Right is int) {
                return Left / (int)Right;
            }
            else if (Right is double) {
                return Left / (double)Right;
            }
            else if (Right is string) {
                var Str = (string)Right;
                var Temp = double.NaN;

                if (double.TryParse(Str, out Temp))
                    return Left / Temp;
            }
            return null;
        }

        public new static bool GreaterThan(double Left, object Right) {
            if (Right is int) {
                return Left > (int)Right;
            }
            else if (Right is double) {
                return Left > (double)Right;
            }
            else if (Right is string) {
                var Str = (string)Right;
                var Temp = double.NaN;

                if (double.TryParse(Str, out Temp))
                    return Left > Temp;
            }
            return false;
        }

        public new static bool LessThan(double Left, object Right) {
            if (Right is int) {
                return Left < (int)Right;
            }
            else if (Right is double) {
                return Left < (double)Right;
            }
            else if (Right is string) {
                var Str = (string)Right;
                var Temp = double.NaN;

                if (double.TryParse(Str, out Temp))
                    return Left < Temp;
            }
            return false;
        }

        public new static bool Equal(double Left, object Right) {
            if (Right is int) {
                return Left == (int)Right;
            }
            else if (Right is double) {
                return Left == (double)Right;
            }
            else if (Right is string) {
                var Str = (string)Right;
                var Temp = double.NaN;

                if (double.TryParse(Str, out Temp))
                    return Left == Temp;
            }
            return false;
        }

        public static Node Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            double Value = 0;

            if (Text.ToDouble(ref Index, LastIndex, ref Value))
                return new Float(Value);

            return null;
        }
    }
}
