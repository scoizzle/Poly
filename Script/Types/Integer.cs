using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Poly.Script.Types {
    using Nodes;
    public class Integer : DataType<int> {
        public int Value;

        public Integer(int Val) {
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
        public new static object Add(int Left, object Right) {
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
            }
            return null;
        }

        public new static object Subtract(int Left, object Right) {
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

        public new static object Multiply(int Left, object Right) {
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

        public new static object Devide(int Left, object Right) {
            if (Right is int) {
                if ((int)Right == 0)
                    return null;

                return Left / (int)Right;
            }
            else if (Right is double) {
                if ((double)Right == 0)
                    return null;
                return Left / (double)Right;
            }
            else if (Right is string) {
                var Str = (string)Right;
                var Temp = double.NaN;

                if (double.TryParse(Str, out Temp)) {
                    if ((int)Temp == 0)
                        return null;

                    return Left / Temp;
                }
            }
            return null;
        }

        public new static bool GreaterThan(int Left, object Right) {
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

        public new static bool LessThan(int Left, object Right) {
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

        public new static bool Equal(int Left, object Right) {
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

            int Value = 0;

            if (Text.ToInt(ref Index, LastIndex, ref Value))
                return new Integer(Value);
            return null;
        }
    }
}
