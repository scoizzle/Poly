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
        #region Add
        public Generator Add() {
            il.Emit(OpCodes.Add);
            return this;
        }

        public Generator Add(string Name1, string Name2) {
            LoadLocal(Name1)
                .LoadLocal(Name2);
            return Add();
        }

        public Generator Add(string Name, int Value) {
            LoadLocal(Name);
            Int(Value);
            return Add();
        }

        public Generator Add(int Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Add();
        }

        public Generator Add(string Name, long Value) {
            LoadLocal(Name);
            Int(Value);
            return Add();
        }

        public Generator Add(long Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Add();
        }

        public Generator Add(string Name, float Value) {
            LoadLocal(Name);
            Float(Value);
            return Add();
        }

        public Generator Add(float Value, string Name) {
            Float(Value);
            LoadLocal(Name);
            return Add();
        }

        public Generator Add(string Name, double Value) {
            LoadLocal(Name);
            Double(Value);
            return Add();
        }

        public Generator Add(double Value, string Name) {
            Double(Value);
            LoadLocal(Name);
            return Add();
        }

        #endregion

        #region Subtract
        public Generator Subtract() {
            il.Emit(OpCodes.Sub);
            return this;
        }

        public Generator Subtract(string Name1, string Name2) {
            LoadLocal(Name1)
                .LoadLocal(Name2);
            return Subtract();
        }

        public Generator Subtract(string Name, int Value) {
            LoadLocal(Name);
            Int(Value);
            return Subtract();
        }

        public Generator Subtract(int Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Subtract();
        }

        public Generator Subtract(string Name, long Value) {
            LoadLocal(Name);
            Int(Value);
            return Subtract();
        }

        public Generator Subtract(long Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Subtract();
        }

        public Generator Subtract(string Name, float Value) {
            LoadLocal(Name);
            Float(Value);
            return Subtract();
        }

        public Generator Subtract(float Value, string Name) {
            Float(Value);
            LoadLocal(Name);
            return Subtract();
        }

        public Generator Subtract(string Name, double Value) {
            LoadLocal(Name);
            Double(Value);
            return Subtract();
        }

        public Generator Subtract(double Value, string Name) {
            Double(Value);
            LoadLocal(Name);
            return Subtract();
        }

        #endregion

        #region Multiply
        public Generator Multiply() {
            il.Emit(OpCodes.Mul);
            return this;
        }

        public Generator Multiply(string Name1, string Name2) {
            LoadLocal(Name1)
                .LoadLocal(Name2);
            return Multiply();
        }

        public Generator Multiply(string Name, int Value) {
            LoadLocal(Name);
            Int(Value);
            return Multiply();
        }

        public Generator Multiply(int Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Multiply();
        }

        public Generator Multiply(string Name, long Value) {
            LoadLocal(Name);
            Int(Value);
            return Multiply();
        }

        public Generator Multiply(long Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Multiply();
        }

        public Generator Multiply(string Name, float Value) {
            LoadLocal(Name);
            Float(Value);
            return Multiply();
        }

        public Generator Multiply(float Value, string Name) {
            Float(Value);
            LoadLocal(Name);
            return Multiply();
        }

        public Generator Multiply(string Name, double Value) {
            LoadLocal(Name);
            Double(Value);
            return Multiply();
        }

        public Generator Multiply(double Value, string Name) {
            Double(Value);
            LoadLocal(Name);
            return Multiply();
        }

        #endregion

        #region Divide
        public Generator Divide() {
            il.Emit(OpCodes.Div);
            return this;
        }

        public Generator DivideUnsigned() {
            il.Emit(OpCodes.Div_Un);
            return this;
        }

        public Generator Divide(string Name1, string Name2) {
            LoadLocal(Name1)
                .LoadLocal(Name2);
            return Divide();
        }

        public Generator DivideUnsigned(string Name1, string Name2) {
            LoadLocal(Name1)
                .LoadLocal(Name2);
            return DivideUnsigned();
        }

        public Generator Divide(string Name, int Value) {
            LoadLocal(Name);
            Int(Value);
            return Divide();
        }

        public Generator Divide(int Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Divide();
        }

        public Generator Divide(string Name, long Value) {
            LoadLocal(Name);
            Int(Value);
            return Divide();
        }

        public Generator Divide(long Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Divide();
        }

        public Generator Divide(string Name, float Value) {
            LoadLocal(Name);
            Float(Value);
            return Divide();
        }

        public Generator Divide(float Value, string Name) {
            Float(Value);
            LoadLocal(Name);
            return Divide();
        }

        public Generator Divide(string Name, double Value) {
            LoadLocal(Name);
            Double(Value);
            return Divide();
        }

        public Generator Divide(double Value, string Name) {
            Double(Value);
            LoadLocal(Name);
            return Divide();
        }

        #endregion

        #region Modulus
        public Generator Modulus() {
            il.Emit(OpCodes.Rem);
            return this;
        }

        public Generator ModulusUnsigned() {
            il.Emit(OpCodes.Rem_Un);
            return this;
        }

        public Generator Modulus(string Name1, string Name2) {
            LoadLocal(Name1)
                .LoadLocal(Name2);
            return Modulus();
        }

        public Generator ModulusUnsigned(string Name1, string Name2) {
            LoadLocal(Name1)
                .LoadLocal(Name2);
            return ModulusUnsigned();
        }

        public Generator Modulus(string Name, int Value) {
            LoadLocal(Name);
            Int(Value);
            return Modulus();
        }

        public Generator Modulus(int Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Modulus();
        }

        public Generator Modulus(string Name, long Value) {
            LoadLocal(Name);
            Int(Value);
            return Modulus();
        }

        public Generator Modulus(long Value, string Name) {
            Int(Value);
            LoadLocal(Name);
            return Modulus();
        }

        public Generator Modulus(string Name, float Value) {
            LoadLocal(Name);
            Float(Value);
            return Modulus();
        }

        public Generator Modulus(float Value, string Name) {
            Float(Value);
            LoadLocal(Name);
            return Modulus();
        }

        public Generator Modulus(string Name, double Value) {
            LoadLocal(Name);
            Double(Value);
            return Modulus();
        }

        public Generator Modulus(double Value, string Name) {
            Double(Value);
            LoadLocal(Name);
            return Modulus();
        }

        #endregion
    }
}
