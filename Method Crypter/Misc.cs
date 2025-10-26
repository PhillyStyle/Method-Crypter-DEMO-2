using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using MethodRenamer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Method_Crypter
{
    internal class Misc
    {
        public static void RandomizeAssemblyGuid(string exePath, string outputExePath)
        {
            string loadEXEPath = "";
            if (File.Exists(outputExePath)) loadEXEPath = outputExePath;
            else loadEXEPath = exePath;

            ModuleDefMD module = ModuleDefMD.Load(loadEXEPath);

            RandomizeAssemblyGuid(module);

            string tempPath = exePath + ".random.guid.tmp.exe";
            try
            {
                var writerOpts = new ModuleWriterOptions(module) { WritePdb = false };
                writerOpts.MetadataOptions.Flags |= dnlib.DotNet.Writer.MetadataFlags.KeepOldMaxStack;
                module.Write(tempPath, writerOpts);
                File.Delete(outputExePath);
                File.Move(tempPath, outputExePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Method renaming caused error:\n" + ex.ToString());
            }
        }

        public static void RandomizeAssemblyGuid(ModuleDefMD module)
        {
            // Get assembly definition
            var asm = module.Assembly;

            // Find GuidAttribute
            var guidAttr = asm.CustomAttributes.Find("System.Runtime.InteropServices.GuidAttribute");

            if (guidAttr != null && guidAttr.ConstructorArguments.Count > 0)
            {
                // Generate new GUID string
                string newGuid = Guid.NewGuid().ToString();

                // Update the attribute argument
                guidAttr.ConstructorArguments[0] = new CAArgument(module.CorLibTypes.String, newGuid);
            }
        }

        public static void ReplaceForceReJitString(string exePath, string outputExePath, List<MethodReplacement> renameList, string methodName, string inType, string inMethod)
        {
            string loadEXEPath = "";
            if (File.Exists(outputExePath)) loadEXEPath = outputExePath;
            else loadEXEPath = exePath;

            ModuleDefMD module = ModuleDefMD.Load(loadEXEPath);

            ReplaceForceReJitString(module, renameList, methodName, inType, inMethod);

            string tempPath = exePath + ".replace.forcerejit.tmp.exe";
            try
            {
                var writerOpts = new ModuleWriterOptions(module) { WritePdb = false };
                writerOpts.MetadataOptions.Flags |= dnlib.DotNet.Writer.MetadataFlags.KeepOldMaxStack;
                module.Write(tempPath, writerOpts);
                File.Delete(outputExePath);
                File.Move(tempPath, outputExePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Force Re-JIT string renaming caused error:\n" + ex.ToString());
            }
        }

        public static void ReplaceForceReJitString(ModuleDefMD module, List<MethodReplacement> renameList, string methodName, string inType, string inMethod)
        {
            if (methodName == "") return; //Only run this if there is a methodName

            //First find methodName in renameList
            string methodNewName = "";

            foreach (var mr in renameList)
            {
                if (mr.OriginalName == methodName)
                {
                    if (methodNewName == "") methodNewName = mr.NewName;
                    else
                    {
                        MessageBox.Show($"Error: (Force Re-JIT string renaming) Too many methods named \"{methodName}\"");
                        return;
                    }
                }
            }

            if (methodNewName == "")
            {
                MessageBox.Show($"Error: (Force Re-JIT string renaming) No method named \"{methodName}\" found!");
                return;
            }

            //Find inType and inMethod
            TypeDef type = module.Find(inType, isReflectionName: true);

            if (type == null)
                throw new Exception($"Type '{inType}' not found.");

            MethodDef method = string.IsNullOrEmpty(inMethod)
                ? type.Methods.First(m => m.Name == ".ctor")  // constructor case
                : type.Methods.First(m => m.Name == inMethod);

            if (method == null)
                throw new Exception($"Method '{method}' not found.");

            var instrs = method.Body.Instructions;

            int occuranceCount = 0;
            foreach (var instr in instrs)
            {
                if (instr.OpCode == OpCodes.Ldstr)
                {
                    //First make sure there is only one occurance of our string in the method.
                    if ((string)instr.Operand  == methodName) occuranceCount++;
                }
            }

            if (occuranceCount == 0)
            {
                MessageBox.Show($"Error: (Force Re-JIT string renaming) No string \"{methodName}\" found in {inType}, {inMethod}!");
                return;
            }

            if (occuranceCount > 1)
            {
                MessageBox.Show($"Error: (Force Re-JIT string renaming) More than one string \"{methodName}\" found in {inType}, {inMethod}!");
                return;
            }

            foreach (var instr in instrs)
            {
                if (instr.OpCode == OpCodes.Ldstr)
                {
                    //First make sure there is only one occurance of our string in the method.
                    if ((string)instr.Operand == methodName)
                    {
                        instr.Operand = methodNewName;
                    }
                }
            }

        }
    }
}
