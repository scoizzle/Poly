using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Float : DataType<double> {
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

        public new static object GreaterThan(double Left, object Right) {
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
            return null;
        }

        public new static object LessThan(double Left, object Right) {
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
            return null;
        }

        public new static object Equal(double Left, object Right) {
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
            return null;
        }

        public static new object Parse(Engine Engine, string Text, ref int Index, int LastIndex) {
            if (!IsParseOk(Engine, Text, ref Index, LastIndex))
                return null;

            var Delta = Index;
            for (; Delta - Index < LastIndex && Delta < Text.Length; Delta++) {
                var C = Text[Delta];

                if (char.IsNumber(C))
                    continue;

                if (C == '+' || C == '-')
                    continue;

                if (C == ',' || C == '.')
                    continue;

                if (C == 'e' || C == 'E')
                    continue;

                if (C == 'f' || C == 'd')
                    break;

                break;
            }

            double Attempt = 0;
            if (double.TryParse(Text.Substring(Index, Delta - Index), out Attempt)) {
                Index = Delta;
                return Attempt;
            }

            return null;
        }
    }
}
