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
        ILGenerator il;
        MethodBuilder Method;
        KeyValueCollection<LocalBuilder> Locals;

        public KeyValueCollection<Label> NamedLabels;

        protected Stack<Label> ls;

        public Label Next { get { return ls.Peek(); } }
        public Label Continue { get; set; }

        public Label PushNext() {
            var Next = il.DefineLabel();
            ls.Push(Next);
            return Next;
        }

        public void PopNext() {
            ls.Pop();
        }

        public delegate void Delegate(Generator Gen);

        public Generator(string Name, Type ReturnType, Type[] ArgumentTypes, TypeBuilder TypeBuilder) {
            Method = TypeBuilder.DefineMethod(
                Name, 
                MethodAttributes.Public,
                ReturnType, 
                ArgumentTypes);

            Init();
        }

        public Generator(string Name, Type ReturnType, Type[] ArgumentTypes, TypeBuilder TypeBuilder, MethodAttributes Attr) {
            Method = TypeBuilder.DefineMethod(
                Name,
                Attr,
                (Attr.HasFlag(MethodAttributes.Static) ? CallingConventions.Standard : CallingConventions.HasThis),
                ReturnType,
                ArgumentTypes);

            Init();
        }

        private void Init() {
            NamedLabels = new KeyValueCollection<Label>();
            Locals = new KeyValueCollection<LocalBuilder>();
            il = Method.GetILGenerator();
            ls = new Stack<Label>();
        }

        public LocalBuilder GetLocal(string Name) {
            return Locals[Name];
        }

        public Generator DeclareLocal(string Name, Type Type) {
            Locals.Add(Name, il.DeclareLocal(Type));
            return this;
        }

        public Generator StoreLocal(string Name) {
            var local = Locals[Name];

            switch (local.LocalIndex) {
                default:
                il.Emit(OpCodes.Stloc, local);
                break;
                case 0:
                il.Emit(OpCodes.Stloc_0);
                break;
                case 1:
                il.Emit(OpCodes.Stloc_1);
                break;
                case 2:
                il.Emit(OpCodes.Stloc_2);
                break;
                case 3:
                il.Emit(OpCodes.Stloc_3);
                break;
            }

            return this;
        }

        public Generator LoadLocal(string Name) {
            var local = Locals[Name];

            switch (local.LocalIndex) {
                default:
                il.Emit(OpCodes.Ldloc, local);
                break;
                case 0:
                il.Emit(OpCodes.Ldloc_0);
                break;
                case 1:
                il.Emit(OpCodes.Ldloc_1);
                break;
                case 2:
                il.Emit(OpCodes.Ldloc_2);
                break;
                case 3:
                il.Emit(OpCodes.Ldloc_3);
                break;
            }

            return this;
        }

        public Generator LoadField(FieldInfo Info) {
            il.Emit(OpCodes.Ldfld, Info);
            return this;
        }

        public Generator StoreField(FieldInfo Info) {
            il.Emit(OpCodes.Stfld, Info);
            return this;
        }

        public Generator LoadProperty(PropertyInfo Info) {
            CallVirtual(Info.GetGetMethod());
            return this;
        }

        public Generator StoreProperty(PropertyInfo Info) {
            CallVirtual(Info.GetSetMethod());
            return this;
        }
        
        public Generator Null() {
            il.Emit(OpCodes.Ldnull);
            return this;
        }

        public Generator Byte() {
            il.Emit(OpCodes.Conv_I1);
            return this;
        }

        public Generator Byte(byte value) {
            il.Emit(OpCodes.Ldc_I4_S, value);
            return this;
        }

        public Generator Short() {
            il.Emit(OpCodes.Conv_I2);
            return this;
        }

        public Generator Short(short value) {
            switch (value) {
                default: {
                    if (value > -129 && value < 128)
                        il.Emit(OpCodes.Ldc_I4_S, (SByte)value);
                    else
                        il.Emit(OpCodes.Ldc_I4, value);
                    break;
                }

                case -1:
                il.Emit(OpCodes.Ldc_I4_M1);
                break;
                case 0:
                il.Emit(OpCodes.Ldc_I4_0);
                break;
                case 1:
                il.Emit(OpCodes.Ldc_I4_1);
                break;
                case 2:
                il.Emit(OpCodes.Ldc_I4_2);
                break;
                case 3:
                il.Emit(OpCodes.Ldc_I4_3);
                break;
                case 4:
                il.Emit(OpCodes.Ldc_I4_4);
                break;
                case 5:
                il.Emit(OpCodes.Ldc_I4_5);
                break;
                case 6:
                il.Emit(OpCodes.Ldc_I4_6);
                break;
                case 7:
                il.Emit(OpCodes.Ldc_I4_7);
                break;
                case 8:
                il.Emit(OpCodes.Ldc_I4_8);
                break;
            }

            return this;
        }

        public Generator Short(ushort value) {
            switch (value) {
                default: {
                    if (value < 256)
                        il.Emit(OpCodes.Ldc_I4_S, (SByte)value);
                    else
                        il.Emit(OpCodes.Ldc_I4, value);
                    break;
                }
                
                case 0:
                il.Emit(OpCodes.Ldc_I4_0);
                break;
                case 1:
                il.Emit(OpCodes.Ldc_I4_1);
                break;
                case 2:
                il.Emit(OpCodes.Ldc_I4_2);
                break;
                case 3:
                il.Emit(OpCodes.Ldc_I4_3);
                break;
                case 4:
                il.Emit(OpCodes.Ldc_I4_4);
                break;
                case 5:
                il.Emit(OpCodes.Ldc_I4_5);
                break;
                case 6:
                il.Emit(OpCodes.Ldc_I4_6);
                break;
                case 7:
                il.Emit(OpCodes.Ldc_I4_7);
                break;
                case 8:
                il.Emit(OpCodes.Ldc_I4_8);
                break;
            }

            return this;
        }

        public Generator Int() {
            il.Emit(OpCodes.Conv_I4);
            return this;
        }

        public Generator Int(int value) {
            switch (value) {
                default: {
                    if (value > -129 && value < 128)
                        il.Emit(OpCodes.Ldc_I4_S, (SByte)value);
                    else
                        il.Emit(OpCodes.Ldc_I4, value);
                    break;
                }

                case -1:
                il.Emit(OpCodes.Ldc_I4_M1);
                break;
                case 0:
                il.Emit(OpCodes.Ldc_I4_0);
                break;
                case 1:
                il.Emit(OpCodes.Ldc_I4_1);
                break;
                case 2:
                il.Emit(OpCodes.Ldc_I4_2);
                break;
                case 3:
                il.Emit(OpCodes.Ldc_I4_3);
                break;
                case 4:
                il.Emit(OpCodes.Ldc_I4_4);
                break;
                case 5:
                il.Emit(OpCodes.Ldc_I4_5);
                break;
                case 6:
                il.Emit(OpCodes.Ldc_I4_6);
                break;
                case 7:
                il.Emit(OpCodes.Ldc_I4_7);
                break;
                case 8:
                il.Emit(OpCodes.Ldc_I4_8);
                break;
            }

            return this;
        }

        public Generator Int(uint value) {
            switch (value) {
                default: {
                    if (value < 128)
                        il.Emit(OpCodes.Ldc_I4_S, (SByte)value);
                    else
                        il.Emit(OpCodes.Ldc_I4, value);
                    break;
                }
                
                case 0:
                il.Emit(OpCodes.Ldc_I4_0);
                break;
                case 1:
                il.Emit(OpCodes.Ldc_I4_1);
                break;
                case 2:
                il.Emit(OpCodes.Ldc_I4_2);
                break;
                case 3:
                il.Emit(OpCodes.Ldc_I4_3);
                break;
                case 4:
                il.Emit(OpCodes.Ldc_I4_4);
                break;
                case 5:
                il.Emit(OpCodes.Ldc_I4_5);
                break;
                case 6:
                il.Emit(OpCodes.Ldc_I4_6);
                break;
                case 7:
                il.Emit(OpCodes.Ldc_I4_7);
                break;
                case 8:
                il.Emit(OpCodes.Ldc_I4_8);
                break;
            }

            return this;
        }

        public Generator Int(long value) {
            il.Emit(OpCodes.Ldc_I8, value);
            return this;
        }

        public Generator Int(ulong value) {
            il.Emit(OpCodes.Ldc_I8, value);
            return this;
        }

        public Generator Float(float value) {
            il.Emit(OpCodes.Ldc_R4, value);
            return this;
        }

        public Generator Double(double value) {
            il.Emit(OpCodes.Ldc_R8, value);
            return this;
        }

        public Generator BoxIfNeeded(Type Type) {
            if (Type.GetTypeInfo().IsValueType)
                il.Emit(OpCodes.Box, Type);

            return this;
        }

        public Generator CastToReference(Type Type) {
            if (Type.GetTypeInfo().IsValueType)
                il.Emit(OpCodes.Unbox_Any, Type);
            else
                il.Emit(OpCodes.Castclass, Type);

            return this;
        }

        public Generator LoadArg(int Index) {
            switch (Index) {
                default:
                il.Emit(OpCodes.Ldarg_S, Index);
                break;
                case 0:
                il.Emit(OpCodes.Ldarg_0);
                break;
                case 1:
                il.Emit(OpCodes.Ldarg_1);
                break;
                case 2:
                il.Emit(OpCodes.Ldarg_2);
                break;
                case 3:
                il.Emit(OpCodes.Ldarg_3);
                break;
            }

            return this;
        }

        public Generator StoreArg(int Index) {
            if (Index > -129 && Index < 128)
                il.Emit(OpCodes.Starg_S, Index);
            else
                il.Emit(OpCodes.Starg, Index);
            return this;
        }

        public Generator NewObject(Type Type, params Type[] argTypes) {
            il.Emit(OpCodes.Newobj, Type.GetConstructor(argTypes));
            return this;
        }

        public Generator NewArray(Type Type, int Length) {
            Int(Length);
            il.Emit(OpCodes.Newarr, Type);
            return this;
        }

        public Generator LoadElement(Type Type) {
            il.Emit(OpCodes.Ldelem, Type);
            return this;
        }

        public Generator LoadElement(Type Type, int Index) {
            Int(Index);
            return LoadElement(Type);
        }

        public Generator StoreElement(Type Type) {
            il.Emit(OpCodes.Stelem, Type);
            return this;
        }

        public Generator StoreElement(Type Type, int Index) {
            Int(Index);
            return StoreElement(Type);
        }

        public Generator Return() {
            il.Emit(OpCodes.Ret);
            return this;
        }

        public Generator Goto(Label Label) {
            il.Emit(OpCodes.Br_S, Label);
            return this;
        }

        public Generator Goto(string Name) {
            Label L;

            if (NamedLabels.TryGetValue(Name, out L)) {
                return Goto(L);
            }
            else {
                L = il.DefineLabel();
                NamedLabels.Add(Name, L);
            }
            return this;
        }

        public Generator Label(string Name) {
            Label L;

            if (NamedLabels.TryGetValue(Name, out L)) {
                il.MarkLabel(L);
            }
            else {
                L = il.DefineLabel();
                NamedLabels.Add(Name, L);
                il.MarkLabel(L);
            }
            return this;
        }

        public Generator Call(MethodInfo Info) {
            il.Emit(OpCodes.Call, Info);
            return this;
        }

        public Generator CallVirtual(MethodInfo Info) {
            il.Emit(OpCodes.Callvirt, Info);
            return this;
        }

        public Generator If(Delegate Conditionals, Delegate Body) {
            var End = PushNext();

            Conditionals(this);
            Body(this);

            PopNext();
            il.MarkLabel(End);
            return this;
        }

        public Generator While(Delegate Conditionals, Delegate Body) {
            Continue = PushNext();
            var End = PushNext();

            il.MarkLabel(Continue);

            Conditionals(this);
            Body(this);

            Goto(Continue);

            il.MarkLabel(End);

            PopNext();
            PopNext();
            return this;
        }

        public Generator Do(Delegate Conditionals, Delegate Body) {
            Continue = PushNext();
            var End = PushNext();

            il.MarkLabel(Continue);

            Body(this);
            Conditionals(this);

            Goto(Continue);

            il.MarkLabel(End);

            PopNext();
            PopNext();
            return this;
        }

        public Generator For(Delegate Init, Delegate Conditionals, Delegate Modifiers, Delegate Body) {
            Continue = PushNext();
            var End = PushNext();

            Init(this);

            il.MarkLabel(Continue);

            Conditionals(this);

            Body(this);
            Modifiers(this);

            Goto(Continue);

            il.MarkLabel(End);

            PopNext();
            PopNext();
            return this;
        }
    }
}
