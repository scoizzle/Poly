using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;


namespace Poly.Compiler {
    using Data;

    public partial class Generator {
        public Generator NotNull(Label Next) {
            il.Emit(OpCodes.Brfalse, Next);
            return this;
        }

        #region =
        public Generator Equal(Label Next) {
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, Next);
            return this;
        }

        public Generator Equal(string Name1, string Name2, Label Next) {
            LoadLocal(Name1)
                .LoadLocal(Name2);

            return Equal(Next);
        }

        public Generator Equal(string Name, int Value, Label Next) {
            LoadLocal(Name)
                .Int(Value);

            return Equal(Next);
        }

        public Generator Equal(int Value, string Name, Label Next) {
            Int(Value)
                .LoadLocal(Name);

            return Equal(Next);
        }

        public Generator Equal(int Value1, int Value2, Label Next) {
            Int(Value1)
                .Int(Value2);

            return Equal(Next);
        }

        public Generator Equal(string Name, float Value, Label Next) {
            LoadLocal(Name)
                .Float(Value);

            return Equal(Next);
        }

        public Generator Equal(float Value, string Name, Label Next) {
            Float(Value)
                .LoadLocal(Name);

            return Equal(Next);
        }

        public Generator Equal(float Value1, float Value2, Label Next) {
            Float(Value1)
                .Float(Value2);

            return Equal(Next);
        }

        public Generator Equal(string Name, double Value, Label Next) {
            LoadLocal(Name)
                .Double(Value);

            return Equal(Next);
        }

        public Generator Equal(double Value, string Name, Label Next) {
            Double(Value)
                .LoadLocal(Name);

            return Equal(Next);
        }

        public Generator Equal(double Value1, double Value2, Label Next) {
            Double(Value1)
                .Double(Value2);

            return Equal(Next);
        }
        #endregion
        
        #region !=
        public Generator NotEqual(Label Next) {
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brtrue, Next);
            return this;
        }

        public Generator NotEqual(string Name1, string Name2, Label Next) {
            LoadLocal(Name1)
                .LoadLocal(Name2);

            return NotEqual(Next);
        }

        public Generator NotEqual(string Name, int Value, Label Next) {
            LoadLocal(Name)
                .Int(Value);

            return NotEqual(Next);
        }

        public Generator NotEqual(int Value, string Name, Label Next) {
            Int(Value)
                .LoadLocal(Name);

            return NotEqual(Next);
        }

        public Generator NotEqual(int Value1, int Value2, Label Next) {
            Int(Value1)
                .Int(Value2);

            return NotEqual(Next);
        }

        public Generator NotEqual(string Name, float Value, Label Next) {
            LoadLocal(Name)
                .Float(Value);

            return NotEqual(Next);
        }

        public Generator NotEqual(float Value, string Name, Label Next) {
            Float(Value)
                .LoadLocal(Name);

            return NotEqual(Next);
        }

        public Generator NotEqual(float Value1, float Value2, Label Next) {
            Float(Value1)
                .Float(Value2);

            return NotEqual(Next);
        }

        public Generator NotEqual(string Name, double Value, Label Next) {
            LoadLocal(Name)
                .Double(Value);

            return NotEqual(Next);
        }

        public Generator NotEqual(double Value, string Name, Label Next) {
            Double(Value)
                .LoadLocal(Name);

            return NotEqual(Next);
        }

        public Generator NotEqual(double Value1, double Value2, Label Next) {
            Double(Value1)
                .Double(Value2);

            return NotEqual(Next);
        }
        #endregion

        #region <
        public Generator LessThan(Label Next) {
            il.Emit(OpCodes.Clt);
            il.Emit(OpCodes.Brfalse, Next);
            return this;
        }

        public Generator LessThanUnsigned(Label Next) {
            il.Emit(OpCodes.Clt_Un);
            il.Emit(OpCodes.Brfalse, Next);
            return this;
        }

        public Generator LessThan(string Name1, string Name2, Label Next) {
            LoadLocal(Name1)
                .LoadLocal(Name2);

            return LessThan(Next);
        }

        public Generator LessThan(string Name, int Value, Label Next) {
            LoadLocal(Name)
                .Int(Value);

            return LessThan(Next);
        }

        public Generator LessThan(int Value, string Name, Label Next) {
            Int(Value)
                .LoadLocal(Name);

            return LessThan(Next);
        }

        public Generator LessThan(int Value1, int Value2, Label Next) {
            Int(Value1)
                .Int(Value2);

            return LessThan(Next);
        }

        public Generator LessThan(string Name, float Value, Label Next) {
            LoadLocal(Name)
                .Float(Value);

            return LessThan(Next);
        }

        public Generator LessThan(float Value, string Name, Label Next) {
            Float(Value)
                .LoadLocal(Name);

            return LessThan(Next);
        }

        public Generator LessThan(float Value1, float Value2, Label Next) {
            Float(Value1)
                .Float(Value2);

            return LessThan(Next);
        }

        public Generator LessThan(string Name, double Value, Label Next) {
            LoadLocal(Name)
                .Double(Value);

            return LessThan(Next);
        }

        public Generator LessThan(double Value, string Name, Label Next) {
            Double(Value)
                .LoadLocal(Name);

            return LessThan(Next);
        }

        public Generator LessThan(double Value1, double Value2, Label Next) {
            Double(Value1)
                .Double(Value2);

            return LessThan(Next);
        }
        #endregion

        #region >
        public Generator GreaterThan(Label Next) {
            il.Emit(OpCodes.Cgt);
            il.Emit(OpCodes.Brfalse, Next);
            return this;
        }

        public Generator GreaterThanUnsigned(Label Next) {
            il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Brfalse, Next);
            return this;
        }

        public Generator GreaterThan(string Name1, string Name2, Label Next) {
            LoadLocal(Name1)
                .LoadLocal(Name2);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(string Name, int Value, Label Next) {
            LoadLocal(Name)
                .Int(Value);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(int Value, string Name, Label Next) {
            Int(Value)
                .LoadLocal(Name);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(int Value1, int Value2, Label Next) {
            Int(Value1)
                .Int(Value2);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(string Name, float Value, Label Next) {
            LoadLocal(Name)
                .Float(Value);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(float Value, string Name, Label Next) {
            Float(Value)
                .LoadLocal(Name);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(float Value1, float Value2, Label Next) {
            Float(Value1)
                .Float(Value2);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(string Name, double Value, Label Next) {
            LoadLocal(Name)
                .Double(Value);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(double Value, string Name, Label Next) {
            Double(Value)
                .LoadLocal(Name);

            return GreaterThan(Next);
        }

        public Generator GreaterThan(double Value1, double Value2, Label Next) {
            Double(Value1)
                .Double(Value2);

            return GreaterThan(Next);
        }
        #endregion
    }
}