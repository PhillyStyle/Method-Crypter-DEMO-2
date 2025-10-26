using dnlib.DotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Method_Crypter
{
    public enum ILFlowControl
    {
        Next,
        Branch,
        Cond_Branch,
        Return,
        Call,
        Throw,
        Break
    }

    public class ILInstruction
    {
        public int Offset { get; set; }
        public string OpCodeName { get; set; }
        public byte OpCodeByte1 { get; set; }
        public byte? OpCodeByte2 { get; set; }
        public int Size { get; set; }
        public ILFlowControl FlowControl { get; set; }
        public object Operand { get; set; }
        public object DnlibOperand { get; set; } // renamed to avoid conflicts
        public uint MDToken { get; set; }
    }


    public static class BinaryPatcher
    {
        private static readonly System.Reflection.Emit.OpCode[] OneByteOpCodes = new System.Reflection.Emit.OpCode[0x100];
        private static readonly System.Reflection.Emit.OpCode[] TwoByteOpCodes = new System.Reflection.Emit.OpCode[0x100];

        public static void DoBinaryPatching(string exePath, List<MethodRenamer.MethodReplacement> renameList, bool useNewNames)
        {
            ModuleDefMD module = ModuleDefMD.Load(exePath);
            Random rng = new Random();
            foreach (var mr in renameList)
            {
                TypeDef type = module.Find(mr.TypeFullName, isReflectionName: true);

                if (type == null)
                    throw new Exception($"Type '{mr.TypeFullName}' not found.");
                var methodName = "";
                // Collect all methods that match methodName (in case of overloaded methods)
                List<MethodDef> methods = null;
                if (useNewNames)
                {
                    methodName = mr.NewName;
                    methods = string.IsNullOrEmpty(mr.NewName)
                        ? type.Methods.Where(m => m.Name == ".ctor").ToList()  // constructor case
                        : type.Methods.Where(m => m.Name == mr.NewName).ToList();
                }
                else
                {
                    methodName = mr.OriginalName;
                    methods = string.IsNullOrEmpty(mr.OriginalName)
                        ? type.Methods.Where(m => m.Name == ".ctor").ToList()  // constructor case
                        : type.Methods.Where(m => m.Name == mr.OriginalName).ToList();
                }

                int i = 0;
                foreach (var method in methods)
                {
                    if (i != mr.overloadIndex)
                    {
                        i++;
                        continue;
                    }

                    byte[] ilBytes = new byte[mr.ILSize];
                    using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
                    {
                        fs.Seek(mr.ILOffsetFile, SeekOrigin.Begin);
                        fs.Read(ilBytes, 0, mr.ILSize);
                    }

                    ilBytes = DoBinaryPatching(exePath, mr.TypeFullName, methodName, mr.overloadIndex, ilBytes, mr.ILSize, mr.IsConstructor, rng);

                    //Write to file!
                    using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Write))
                    {
                        fs.Seek(mr.ILOffsetFile, SeekOrigin.Begin);
                        fs.Write(ilBytes, 0, mr.ILSize);
                    }
                }
            }

            module.Dispose();
        }

        public static byte[] DoBinaryPatching(string exePath, string typeName, string methodName, int overloadIndex, byte[] ilBytes, int ilSize, bool isConstructor, Random rng)
        {
            InitOpCodes();

            ModuleDefMD module = ModuleDefMD.Load(exePath);
            TypeDef type = module.Find(typeName, isReflectionName: true);

            if (type == null)
                throw new Exception($"Type '{typeName}' not found.");

            // Collect all methods that match methodName (in case of overloaded methods)
            List<MethodDef> methods = null;

            methods = string.IsNullOrEmpty(methodName)
                ? type.Methods.Where(m => m.Name == ".ctor").ToList()  // constructor case
                : type.Methods.Where(m => m.Name == methodName).ToList();

            int k = 0;
            MethodDef method = null;
            foreach (MethodDef m in methods)
            {
                if (k != overloadIndex)
                {
                    k++;
                    continue;
                }
                method = m;
            }
            if (method == null)
                throw new Exception($"Method '{methodName}' not found.");

            // Create a resolver context so dnlib can find referenced assemblies
            ModuleContext context = new ModuleContext(new AssemblyResolver());
            module.Context = context;
            ((AssemblyResolver)context.AssemblyResolver).AddToCache(module.Assembly);
            ((AssemblyResolver)context.AssemblyResolver).EnableTypeDefCache = true;

            // Make sure the resolver knows where to look for system assemblies
            ((AssemblyResolver)context.AssemblyResolver).PostSearchPaths.Add(RuntimeEnvironment.GetRuntimeDirectory());

            List<ILInstruction> instrs = ReadInstructionsToList(ilBytes, method);
            List<List<ILInstruction>> preserve = GetPreserveList(instrs, method, isConstructor);


            //Handle local variables
            var dnlibInstrs = method.Body.Instructions;
            var instrsCopy = instrs.ToList();
            int curinstrsIndex = 0;
            if (method.Body.Variables.Count > 0)
            {
                for (int i = 0; i < instrsCopy.Count; i++)
                {
                    var inst = instrsCopy[i];
                    var name = inst.OpCodeName;
                    byte[] patchBytes = null;
                    bool gotPatchBytes = false;
                    // Match any stloc variant
                    if (name == "stloc" || name == "stloc.s" ||
                        name == "stloc.0" || name == "stloc.1" ||
                        name == "stloc.2" || name == "stloc.3")
                    {
                        // Determine the variable index being stored to
                        //Find the instruction list in preserve that contains this instruction

                        List<ILInstruction> curPreserve = FindInstructionListWithOffset(instrsCopy[i].Offset, preserve);
                        if (curPreserve == null) continue; //This should never happen

                        int varIndex;
                        switch (name)
                        {
                            case "stloc.0": varIndex = 0; break;
                            case "stloc.1": varIndex = 1; break;
                            case "stloc.2": varIndex = 2; break;
                            case "stloc.3": varIndex = 3; break;
                            default:
                                // For stloc/stloc.s, the operand is a Local
                                varIndex = -1;
                                if (inst.Operand is byte)
                                    varIndex = (byte)inst.Operand;
                                else if (inst.Operand is sbyte)
                                    varIndex = (sbyte)inst.Operand;
                                else if (inst.Operand is short)
                                    varIndex = (short)inst.Operand;
                                else if (inst.Operand is ushort)
                                    varIndex = (ushort)inst.Operand;
                                else if (inst.Operand is int)
                                    varIndex = (int)inst.Operand;
                                break;
                        }

                        ElementType metaType = ElementType.I4;
                        if (i > 0 && (instrsCopy[i - 1].OpCodeName == "call" || instrsCopy[i - 1].OpCodeName == "callvirt"))
                        {
                            var calledMethod = dnlibInstrs[i - 1].Operand as IMethod;  //Assuming dnlib's instructions are alligned with ours.
                            if (calledMethod != null)
                            {
                                try
                                {
                                    TypeDef typeX = null;
                                    uint mdToken = 0;

                                    // Try to resolve the actual MethodDef first
                                    MethodDef resolvedMethod = calledMethod.ResolveMethodDef();

                                    if (resolvedMethod != null)
                                    {
                                        // Local method, easy mode
                                        var retSig = resolvedMethod.MethodSig.RetType;
                                        var retRef = retSig.ToTypeDefOrRef();
                                        if (retRef != null)
                                        {
                                            metaType = retSig.GetElementType();
                                            typeX = retRef.ResolveTypeDef();
                                            mdToken = retRef.MDToken.Raw;
                                        }
                                    }
                                    else
                                    {
                                        // Cross-assembly fallback
                                        MethodSig sig = calledMethod.MethodSig;
                                        ITypeDefOrRef retTypeRef = sig?.RetType?.ToTypeDefOrRef();

                                        if (retTypeRef != null)
                                        {
                                            mdToken = retTypeRef.MDToken.Raw;
                                            typeX = retTypeRef.ResolveTypeDef();

                                            // If it's null, we might need to load the defining assembly manually
                                            if (typeX == null && calledMethod.DeclaringType != null)
                                            {
                                                var declType = calledMethod.DeclaringType;
                                                var asmName = declType.DefinitionAssembly?.FullName;

                                                if (!string.IsNullOrEmpty(asmName))
                                                {
                                                    try
                                                    {
                                                        // Load the external assembly where the return type is defined
                                                        var asmPath = Path.Combine(Path.GetDirectoryName(module.Location), asmName.Split(',')[0] + ".dll");
                                                        if (File.Exists(asmPath))
                                                        {
                                                            ModuleDef extModule = ModuleDefMD.Load(asmPath);

                                                            // Try again in that module
                                                            TypeDef extType = retTypeRef.ResolveTypeDef();
                                                            if (extType == null)
                                                            {
                                                                var nameX = retTypeRef.FullName;
                                                                extType = extModule.Types.FirstOrDefault(t => t.FullName == nameX);
                                                            }
                                                            mdToken = retTypeRef.MDToken.Raw;
                                                            typeX = extType;
                                                        }
                                                    }
                                                    catch (Exception loadEx)
                                                    {
                                                        MessageBox.Show($"Failed to load external assembly: {loadEx.Message}");
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    // At this point, typeX should be the TypeDef of the return type
                                    if (typeX != null)
                                    {
                                        bool isStruct = typeX.IsValueType && !typeX.IsEnum;

                                        if (isStruct)
                                        {
                                            var resolvedToken = module.ResolveToken(mdToken);
                                            if (resolvedToken != null)
                                            {
                                                byte[] pb1 = GetBytesForLiteral("ldloca.s", (byte)0);
                                                byte[] pb2 = GetBytesForLiteral("initobj", resolvedToken.MDToken.Raw);
                                                byte[] pb3 = GetBytesForLiteral("ldloc.0");
                                                patchBytes = new byte[pb1.Length + pb2.Length + pb3.Length];
                                                Buffer.BlockCopy(pb1, 0, patchBytes, 0, pb1.Length);
                                                Buffer.BlockCopy(pb2, 0, patchBytes, pb1.Length, pb2.Length);
                                                Buffer.BlockCopy(pb3, 0, patchBytes, pb1.Length + pb2.Length, pb3.Length);
                                                gotPatchBytes = true;
                                            }
                                            else
                                            {
                                                //cut our losses and dump this part.
                                                //(For very complex reasons)
                                                preserve.Remove(curPreserve);
                                                continue;
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"Error resolving {calledMethod.FullName}: {ex.Message}");
                                }
                            }

                            //remove call from curPreserve
                            var curPreserveCopy = curPreserve.ToList();
                            for (int p = 0; p < curPreserveCopy.Count; p++)
                            {
                                if (curPreserveCopy[p].OpCodeName == "call" || curPreserveCopy[p].OpCodeName == "callvirt")
                                {
                                    curPreserve.RemoveAt(p);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Skip invalid or out-of-range locals
                            if (varIndex < 0 || varIndex >= method.Body.Variables.Count)
                                continue;

                            var variable = method.Body.Variables[varIndex];
                            metaType = variable.Type.ElementType;
                        }

                        if (i > 0)
                        {
                            if (!gotPatchBytes)
                            {
                                switch (metaType)
                                {
                                    case ElementType.I1:
                                    case ElementType.U1:
                                    case ElementType.I2:
                                    case ElementType.U2:
                                    case ElementType.I4:
                                    case ElementType.U4:
                                    case ElementType.Boolean:
                                        patchBytes = GetBytesForLiteral("ldc.i4.0");
                                        break;

                                    case ElementType.I8:
                                    case ElementType.U8:
                                        patchBytes = GetBytesForLiteral("ldc.i8", 0L);
                                        break;

                                    case ElementType.R4:
                                        patchBytes = GetBytesForLiteral("ldc.r4", 0.0f);
                                        break;

                                    case ElementType.R8:
                                        patchBytes = GetBytesForLiteral("ldc.r8", 0.0d);
                                        break;

                                    case ElementType.ValueType:
                                        //Don't know what it is, have to dump it.
                                        preserve.Remove(curPreserve);
                                        continue;

                                    case ElementType.Var:
                                        //We can't tell what var is, so we dump it for saftey reasons
                                        preserve.Remove(curPreserve);
                                        continue;

                                    default:
                                        patchBytes = GetBytesForLiteral("ldnull");
                                        break;
                                }
                            }

                            if (patchBytes != null && patchBytes.Length > 0)
                            {
                                //Patching before Inst
                                var tehPatch = ReadInstructionsToList(patchBytes, method);

                                int patchIndex = 0;
                                foreach (var teh in tehPatch)
                                    curPreserve.Insert(patchIndex++, teh);
                            }
                        }
                    }
                    curinstrsIndex++;
                }
            }

            //Get Total size of our preserved code
            int preservedSum = 0;
            foreach (var p in preserve)
                foreach (var instruction in p)
                    preservedSum += instruction.Size;

            int[] gaps = CalculateRandomGaps(preservedSum, preserve.Count, ilSize - 1, rng, !isConstructor); //Leave a byte so we can put ret at the end

            int preserveIndex = 0;
            byte[] encoded = null;
            if (isConstructor)
            {
                encoded = ILEncoder.Encode(preserve[preserveIndex], module);
                preserveIndex++;
            }

            //Generate Code and Fill Gaps
            //Trusting our gap generation code for bounds checking
            int ilBytesIndex = 0;
            int gapIndex = 0;
            while (ilBytesIndex < ilSize - 1)
            {
                if (encoded != null)
                {
                    Buffer.BlockCopy(encoded, 0, ilBytes, ilBytesIndex, encoded.Length);
                    ilBytesIndex += encoded.Length;
                }
                byte[] generated = GenNeutralCode(gaps[gapIndex], rng);
                gapIndex++;
                Buffer.BlockCopy(generated, 0, ilBytes, ilBytesIndex, generated.Length);
                ilBytesIndex += generated.Length;
                if (preserveIndex < preserve.Count)
                {
                    encoded = ILEncoder.Encode(preserve[preserveIndex], module);
                    preserveIndex++;
                }
            }

            //Don't forget return at the end!
            Buffer.SetByte(ilBytes, ilBytes.Length - 1, 0x2A);

            module.Dispose();
            return ilBytes;
        }

        public static byte[] GenNeutralCode(int len, Random rng)
        {
            byte[] ilOut = new byte[len];

            //Finally Fill in with junk code
            int n = 0;
            while (n < len)
            {
                // Generate neutral code bytes for this slot
                byte[] nc = GetNeutralCode(len - n, rng);
                Buffer.BlockCopy(nc, 0, ilOut, n, nc.Length);
                n += nc.Length;
            }
            return ilOut;
        }

        public static int[] CalculateRandomGaps(int sectionSizesSum, int sectionSizesCount, int ilSize, Random rng, bool includeLeadingGap = false)
        {
            int sectionsTotal = sectionSizesSum;
            int gapsCount = sectionSizesCount - 1 + (includeLeadingGap ? 1 : 0) + 1; // +1 for ending gap
            int totalFiller = ilSize - sectionsTotal;

            if (totalFiller < 0)
                throw new ArgumentException("Total section sizes exceed ilSize.");

            // Generate random cut points
            int[] cuts = new int[gapsCount - 1];
            for (int i = 0; i < cuts.Length; i++)
                cuts[i] = rng.Next(0, totalFiller + 1);

            Array.Sort(cuts);

            // Compute filler sizes as differences between cuts
            int[] fillerSizes = new int[gapsCount];
            int previous = 0;
            for (int i = 0; i < cuts.Length; i++)
            {
                fillerSizes[i] = cuts[i] - previous;
                previous = cuts[i];
            }
            fillerSizes[gapsCount - 1] = totalFiller - previous;

            return fillerSizes;
        }

        private static byte[] GetBytesForLiteral(string opcodeName, object value = null)
        {
            switch (opcodeName)
            {
                // Integers
                case "ldc.i4.0": return new byte[] { 0x16 };
                case "ldc.i4.1": return new byte[] { 0x17 };
                case "ldc.i4.2": return new byte[] { 0x18 };
                case "ldc.i4.3": return new byte[] { 0x19 };
                case "ldc.i4.4": return new byte[] { 0x1A };
                case "ldc.i4.5": return new byte[] { 0x1B };
                case "ldc.i4.6": return new byte[] { 0x1C };
                case "ldc.i4.7": return new byte[] { 0x1D };
                case "ldc.i4.8": return new byte[] { 0x1E };
                case "ldloc.0": return new byte[] { 0x06 };
                case "ldloc.1": return new byte[] { 0x07 };
                case "ldloc.2": return new byte[] { 0x08 };
                case "ldloc.3": return new byte[] { 0x09 };

                case "ldc.i4.s":
                    return new byte[] { 0x1F, (byte)(sbyte)value };

                case "ldc.i4":
                    {
                        int v = (int)value;
                        return new byte[]
                        {
                            0x20,
                            (byte)(v & 0xFF),
                            (byte)((v >> 8) & 0xFF),
                            (byte)((v >> 16) & 0xFF),
                            (byte)((v >> 24) & 0xFF)
                        };
                    }

                case "ldc.i8":
                    {
                        long v = (long)value;
                        return new byte[]
                        {
                            0x21,
                            (byte)(v & 0xFF),
                            (byte)((v >> 8) & 0xFF),
                            (byte)((v >> 16) & 0xFF),
                            (byte)((v >> 24) & 0xFF),
                            (byte)((v >> 32) & 0xFF),
                            (byte)((v >> 40) & 0xFF),
                            (byte)((v >> 48) & 0xFF),
                            (byte)((v >> 56) & 0xFF)
                        };
                    }

                case "ldnull":
                    return new byte[] { 0x14 };

                case "ldc.r4":
                    {
                        float f = (float)value;
                        byte[] bytes = BitConverter.GetBytes(f);
                        byte[] result = new byte[1 + 4];
                        result[0] = 0x22;
                        Array.Copy(bytes, 0, result, 1, 4);
                        return result;
                    }

                case "ldc.r8":
                    {
                        double d = (double)value;
                        byte[] bytes = BitConverter.GetBytes(d);
                        byte[] result = new byte[1 + 8];
                        result[0] = 0x23;
                        Array.Copy(bytes, 0, result, 1, 8);
                        return result;
                    }

                // New support for structs (ldloca.s + initobj)
                case "ldloca.s":
                    {
                        // Operand is 1-byte local variable index
                        if (value == null)
                            throw new ArgumentNullException("value", "ldloca.s requires a local index (byte).");

                        byte localIndex = Convert.ToByte(value);
                        return new byte[] { 0x12, localIndex };
                    }

                case "initobj":
                    {
                        // Operand is 4-byte metadata token (uint)
                        if (value == null)
                            throw new ArgumentNullException("value", "initobj requires a 4-byte metadata token.");

                        uint token = Convert.ToUInt32(value);
                        return new byte[]
                        {
                            0xFE, 0x15,
                            (byte)(token & 0xFF),
                            (byte)((token >> 8) & 0xFF),
                            (byte)((token >> 16) & 0xFF),
                            (byte)((token >> 24) & 0xFF)
                        };
                    }

                default:
                    throw new InvalidOperationException($"Opcode {opcodeName} not handled");
            }
        }



        static readonly byte[][] NeutralPatterns = new[] {
            new byte[] { 0x16, 0x26 }, //0x16 == ldc.i4.0, 0x26 == pop
            new byte[] { 0x17, 0x18, 0x58, 0x26 }, //0x17 == ldc.i4.1, 0x18 == ldc.i4.2, 0x58 = add, 0x26 == pop
            new byte[] { 0x14, 0x26 }, //0x14 == ldnull, 0x26 == pop
            new byte[] { 0x19, 0x1A, 0x58, 0x26 }, //0x19 == ldc.i4.3, 0x1A == ldc.i4.4, 0x58 == add, 0x26 == pop
            new byte[] { 0x16, 0x69, 0x26 } //0x16 == ldc.i4.0, 0x69 == conv.i8, 0x26 == pop
        };


        static byte[] GetNeutralCode(int insAvailible, Random rng)
        {
            if (insAvailible == 1)
            {
                return new byte[] { 0x00 }; //0x00 == NOP
            }

            byte[] pattern = null;

            do
            {
                pattern = NeutralPatterns[rng.Next(NeutralPatterns.Length)];
            }
            while (pattern.Length > insAvailible);

            return pattern;
        }

        private static List<ILInstruction> FindInstructionListWithOffset(int offset, List<List<ILInstruction>> preserve)
        {
            foreach (List<ILInstruction> ilList in preserve)
            {
                foreach (ILInstruction instruction in ilList)
                {
                    if (instruction.Offset == offset) return ilList;
                }
            }
            return null;
        }

        private static List<List<ILInstruction>> GetPreserveList(List<ILInstruction> instrs, MethodDef method, bool isConstructor)
        {
            List<List<ILInstruction>> preserve = new List<List<ILInstruction>>();

            // Preserve prolog for constructors
            if (isConstructor)
            {
                List<ILInstruction> constructorBypassInitial = new List<ILInstruction>();
                for (int i = 0; i < instrs.Count; i++)
                {
                    var inst = instrs[i];
                    if (inst.OpCodeName == "call" || inst.OpCodeName == "callvirt")
                    {
                        // include this call and stop
                        constructorBypassInitial.Add(inst);
                        break;
                    }
                    else if ((inst.FlowControl == ILFlowControl.Branch) || (inst.FlowControl == ILFlowControl.Cond_Branch)) // use flow control now
                    {
                        // include this call and stop
                        constructorBypassInitial.Add(inst);
                        break;
                    }
                    else
                    {
                        constructorBypassInitial.Add(inst);
                    }
                }
                if (constructorBypassInitial.Count > 0) preserve.Add(constructorBypassInitial);
            }

            //Preserve local variables and ldstrs
            for (int i = 0; i < instrs.Count; i++)
            {
                var inst = instrs[i];
                switch (inst.OpCodeName)
                {
                    case "stloc":
                    case "stloc.s":
                    case "stloc.0":
                    case "stloc.1":
                    case "stloc.2":
                    case "stloc.3":
                        List<ILInstruction> storeLocal = new List<ILInstruction>();
                        storeLocal.Add(inst);
                        if (i > 0 && (instrs[i - 1].OpCodeName == "call" || instrs[i - 1].OpCodeName == "callvirt"))
                            storeLocal.Insert(0, instrs[i - 1]);
                        preserve.Add(storeLocal);
                        break;
                    case "ldstr":
                        List<ILInstruction> loadString = new List<ILInstruction>();
                        loadString.Add(inst);
                        byte[] pop = new byte[] { 0x26 };
                        loadString.Add(ReadInstructionsToList(pop, method)[0]);
                        preserve.Add(loadString);
                        break;
                }
            }

            return preserve;
        }

        private static List<ILInstruction> ReadInstructionsToList(byte[] ilBytes, MethodDef methodDef)
        {
            var instrs = new List<ILInstruction>();
            int pos = 0;
            var dnlibInstrs = methodDef.Body.Instructions;

            while (pos < ilBytes.Length)
            {
                int start = pos;
                byte code = ilBytes[pos++];

                System.Reflection.Emit.OpCode op;
                byte? second = null;

                if (code == 0xFE)
                {
                    second = ilBytes[pos++];
                    op = TwoByteOpCodes[second.Value];
                }
                else
                {
                    op = OneByteOpCodes[code];
                }

                object operand = null;
                object dnlibOperand = null;
                uint mdToken = 0;
                int operandSize = 0;

                switch (op.OperandType)
                {
                    case System.Reflection.Emit.OperandType.InlineNone:
                        break;

                    case System.Reflection.Emit.OperandType.ShortInlineI:
                        operand = (sbyte)ilBytes[pos];
                        operandSize = 1;
                        break;

                    case System.Reflection.Emit.OperandType.InlineI:
                        operand = BitConverter.ToInt32(ilBytes, pos);
                        operandSize = 4;
                        break;

                    case System.Reflection.Emit.OperandType.InlineI8:
                        operand = BitConverter.ToInt64(ilBytes, pos);
                        operandSize = 8;
                        break;

                    case System.Reflection.Emit.OperandType.ShortInlineR:
                        operand = BitConverter.ToSingle(ilBytes, pos);
                        operandSize = 4;
                        break;

                    case System.Reflection.Emit.OperandType.InlineR:
                        operand = BitConverter.ToDouble(ilBytes, pos);
                        operandSize = 8;
                        break;

                    case System.Reflection.Emit.OperandType.ShortInlineVar:
                        operand = ilBytes[pos];
                        operandSize = 1;
                        break;

                    case System.Reflection.Emit.OperandType.InlineVar:
                        operand = BitConverter.ToUInt16(ilBytes, pos);
                        operandSize = 2;
                        break;

                    case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                        operand = (sbyte)ilBytes[pos] + pos + 1;
                        operandSize = 1;
                        break;

                    case System.Reflection.Emit.OperandType.InlineBrTarget:
                        operand = BitConverter.ToInt32(ilBytes, pos) + pos + 4;
                        operandSize = 4;
                        break;

                    case System.Reflection.Emit.OperandType.InlineSwitch:
                        int count = BitConverter.ToInt32(ilBytes, pos);
                        operandSize = 4 + (count * 4);
                        int[] targets = new int[count];
                        for (int i = 0; i < count; i++)
                        {
                            targets[i] = BitConverter.ToInt32(ilBytes, pos + 4 + (i * 4)) + pos + 4 + (count * 4);
                        }
                        operand = targets;
                        break;

                    case System.Reflection.Emit.OperandType.InlineString:
                    case System.Reflection.Emit.OperandType.InlineMethod:
                    case System.Reflection.Emit.OperandType.InlineField:
                    case System.Reflection.Emit.OperandType.InlineType:
                    case System.Reflection.Emit.OperandType.InlineTok:
                        int token = BitConverter.ToInt32(ilBytes, pos);
                        operand = token;

                        // Lookup dnlib instruction if available
                        var matchingInstr = dnlibInstrs.FirstOrDefault(d => d.Offset == start);
                        if (matchingInstr != null)
                        {
                            if (matchingInstr.Operand is IMDTokenProvider mdProvider)
                            {
                                dnlibOperand = mdProvider;
                                mdToken = mdProvider.MDToken.Raw;
                            }
                            else if (matchingInstr.Operand is string str)
                            {
                                dnlibOperand = str;
                                mdToken = (uint)token;
                            }
                        }
                        else
                        {
                            mdToken = (uint)token;
                        }

                        operandSize = 4;
                        break;

                    default:
                        operandSize = GetOperandSize(op.OperandType);
                        break;
                }

                pos += operandSize;

                instrs.Add(new ILInstruction
                {
                    Offset = start,
                    OpCodeName = op.Name,
                    OpCodeByte1 = code,
                    OpCodeByte2 = second,
                    Size = pos - start,
                    FlowControl = MapFlowControl(op),
                    Operand = operand,
                    DnlibOperand = dnlibOperand,
                    MDToken = mdToken
                });
            }

            return instrs;
        }



        private static void InitOpCodes()
        {
            // Fill one-byte opcodes
            foreach (var fi in typeof(System.Reflection.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (fi.GetValue(null) is System.Reflection.Emit.OpCode op)
                {
                    ushort value = (ushort)op.Value;
                    if ((value & 0xFF00) == 0xFE00) // two-byte opcode
                        TwoByteOpCodes[value & 0xFF] = op;
                    else
                        OneByteOpCodes[value] = op;
                }
            }
        }

        private static int GetOperandSize(System.Reflection.Emit.OperandType type)
        {
            switch (type)
            {
                case System.Reflection.Emit.OperandType.InlineBrTarget:
                case System.Reflection.Emit.OperandType.InlineField:
                case System.Reflection.Emit.OperandType.InlineI:
                case System.Reflection.Emit.OperandType.InlineMethod:
                case System.Reflection.Emit.OperandType.InlineSig:
                case System.Reflection.Emit.OperandType.InlineString:
                case System.Reflection.Emit.OperandType.InlineTok:
                case System.Reflection.Emit.OperandType.InlineType:
                    return 4;
                case System.Reflection.Emit.OperandType.InlineSwitch:
                    return 4; // simplified, actual is 4*n+4
                case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                case System.Reflection.Emit.OperandType.ShortInlineI:
                case System.Reflection.Emit.OperandType.ShortInlineVar:
                    return 1;
                case System.Reflection.Emit.OperandType.InlineVar:
                    return 2;
                case System.Reflection.Emit.OperandType.InlineI8:
                case System.Reflection.Emit.OperandType.InlineR:
                    return 8;
                case System.Reflection.Emit.OperandType.InlineNone:
                default:
                    return 0;
            }
        }

        // Map dnlib OpCode to our simplified ILFlowControl enum
        private static ILFlowControl MapFlowControl(System.Reflection.Emit.OpCode op)
        {
            switch (op.FlowControl)
            {
                case System.Reflection.Emit.FlowControl.Branch:
                    return ILFlowControl.Branch;
                case System.Reflection.Emit.FlowControl.Cond_Branch:
                    return ILFlowControl.Cond_Branch;
                case System.Reflection.Emit.FlowControl.Call:
                    return ILFlowControl.Call;
                case System.Reflection.Emit.FlowControl.Return:
                    return ILFlowControl.Return;
                case System.Reflection.Emit.FlowControl.Throw:
                    return ILFlowControl.Throw;
                case System.Reflection.Emit.FlowControl.Next:
                    return ILFlowControl.Next;
                case System.Reflection.Emit.FlowControl.Break:
                    return ILFlowControl.Break;
                default:
                    return ILFlowControl.Next;
            }
        }
    }

    public static class ILEncoder
    {
        private static readonly Dictionary<string, System.Reflection.Emit.OpCode> NameToOpCode;

        static ILEncoder()
        {
            // Build name→opcode lookup
            NameToOpCode = typeof(System.Reflection.Emit.OpCodes)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
                .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null))
                .ToDictionary(o => o.Name, o => o, StringComparer.OrdinalIgnoreCase);
        }

        public static byte[] Encode(List<ILInstruction> instructions, ModuleDef module)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                foreach (var instr in instructions)
                {
                    if (!NameToOpCode.TryGetValue(instr.OpCodeName, out var opcode))
                        throw new InvalidOperationException($"Unknown opcode: {instr.OpCodeName}");

                    ushort value = (ushort)opcode.Value;
                    byte op1 = (byte)(value >> 8);   // high byte (prefix)
                    byte op2 = (byte)(value & 0xFF); // low byte (actual opcode)

                    // Write opcode bytes
                    if (opcode.Size == 2)
                    {
                        bw.Write(op1);
                        bw.Write(op2);
                    }
                    else
                    {
                        bw.Write(op2);
                    }

                    // Write operand depending on opcode type
                    switch (opcode.OperandType)
                    {
                        case System.Reflection.Emit.OperandType.InlineNone:
                            break;

                        case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                        case System.Reflection.Emit.OperandType.ShortInlineI:
                        case System.Reflection.Emit.OperandType.ShortInlineVar:
                            bw.Write(Convert.ToByte(instr.Operand));
                            break;

                        case System.Reflection.Emit.OperandType.InlineVar:
                            bw.Write(Convert.ToUInt16(instr.Operand));
                            break;

                        case System.Reflection.Emit.OperandType.InlineI:
                            bw.Write(Convert.ToInt32(instr.Operand));
                            break;

                        case System.Reflection.Emit.OperandType.InlineI8:
                            bw.Write(Convert.ToInt64(instr.Operand));
                            break;

                        case System.Reflection.Emit.OperandType.InlineR:
                            bw.Write(Convert.ToDouble(instr.Operand));
                            break;

                        case System.Reflection.Emit.OperandType.ShortInlineR:
                            bw.Write(Convert.ToSingle(instr.Operand));
                            break;

                        // Metadata / strings using dnlib
                        case System.Reflection.Emit.OperandType.InlineMethod:
                        case System.Reflection.Emit.OperandType.InlineType:
                        case System.Reflection.Emit.OperandType.InlineField:
                        case System.Reflection.Emit.OperandType.InlineTok:
                            {
                                if (instr.DnlibOperand is IMDTokenProvider mdOperand)
                                {
                                    bw.Write(mdOperand.MDToken.Raw);
                                }
                                else
                                {
                                    bw.Write(instr.MDToken);
                                }
                            }
                            break;

                        case System.Reflection.Emit.OperandType.InlineString:
                            {
                                if (instr.DnlibOperand is string)
                                {
                                    // Get token for string
                                    uint token = instr.MDToken;

                                    bw.Write(token);
                                }
                                else
                                {
                                    throw new InvalidOperationException("DnlibOperand must be a string for InlineString opcode");
                                }
                            }
                            break;

                        default:
                            throw new NotSupportedException($"Operand type {opcode.OperandType} not yet supported for encoding.");
                    }
                }

                return ms.ToArray();
            }
        }

    }
}

