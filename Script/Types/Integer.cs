using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Poly.Script.Node {
    public class Integer : DataType<int> {
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

                return Left + (string)Right;
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

        public new static object GreaterThan(int Left, object Right) {
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

        public new static object LessThan(int Left, object Right) {
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

        public new static object Equal(int Left, object Right) {
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
            for (; Delta - Index < LastIndex && Delta < LastIndex; Delta++) {
                var C = Text[Delta];

                if (char.IsNumber(C))
                    continue;

                if (C == '+' || C == '-')
                    continue;

                if (C == ',' || C == '.')
                    continue;

                if (C == 'e' || C == 'E')
                    continue;

                break;
            }

            int Attempt = 0;
            if (int.TryParse(Text.Substring(Index, Delta - Index), out Attempt)) {
                Index = Delta;
                return Attempt;
            }

            return null;
        }
    }
}
