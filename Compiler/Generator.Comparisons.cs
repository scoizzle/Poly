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
        public Generator LessThan(Label Next) {
            il.Emit(OpCodes.Clt);
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
    }
}