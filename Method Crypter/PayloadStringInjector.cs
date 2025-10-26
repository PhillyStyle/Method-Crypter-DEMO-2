using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PayloadStringInjector
{
    public static class PayloadStringInjector
    {
        public static void ReplaceStringsArray(string exePath, List<MethodRenamer.MethodReplacement> renameList, string plStringsType, string plStringsArrayName, byte[] key, byte[] iv, bool useNewNames)
        {
            ModuleDefMD module = ModuleDefMD.Load(exePath);

            module = ReplaceStringsArray(module, exePath, renameList, plStringsType, plStringsArrayName, key, iv, useNewNames);

            string tempPath = exePath + ".strings.replace.tmp.exe";
            try
            {
                var writerOpts = new ModuleWriterOptions(module) { WritePdb = false };
                writerOpts.MetadataOptions.Flags |= dnlib.DotNet.Writer.MetadataFlags.KeepOldMaxStack;
                module.Write(tempPath, writerOpts);
                File.Delete(exePath);
                File.Move(tempPath, exePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("String injecting caused error:\n" + ex.ToString());
            }
        }


        public static ModuleDefMD ReplaceStringsArray(ModuleDefMD module, string pathName, List<MethodRenamer.MethodReplacement> renameList, string plStringsType, string plStringsArrayName, byte[] key, byte[] iv, bool useNewNames)
        {
            TypeDef targetType = null;
            FieldDef field = null;
            try
            {
                // Find the target type
                targetType = module.Types.First(t => t.FullName == plStringsType);

                // Find the static field
                field = targetType.Fields.First(f => f.Name == plStringsArrayName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error Finding {plStringsType}, {plStringsArrayName}:\n" + ex.ToString());
                return module;
            }

            if (targetType == null || field == null)
            {
                MessageBox.Show($"Error Finding {plStringsType}, {plStringsArrayName}.");
                return module;
            }

            // Get the static constructor (.cctor)
            var cctor = targetType.FindStaticConstructor();
            if (cctor == null)
            {
                MessageBox.Show($"Error Finding {plStringsType}, {plStringsArrayName} constructor.");
                return module;
            }

            var il = cctor.Body.Instructions;
            il.Clear();

            List<string> newStrings = new List<string>();

            int eIndex = 0;
            //Populate newStrings
            foreach (var mr in renameList)
            {
                string encrypted = "";
                if (useNewNames) encrypted = Method_Crypter.EncryptMethod.GetEncryptedMethod(module, pathName, mr.overloadIndex, mr.TypeFullName, mr.NewName, key, iv);
                else encrypted = Method_Crypter.EncryptMethod.GetEncryptedMethod(module, pathName, mr.overloadIndex, mr.TypeFullName, mr.OriginalName, key, iv);
                newStrings.Add(encrypted);
                mr.EncryptedIndex = eIndex;
                eIndex++;
            }

            // Emit IL to create and populate string[]
            il.Add(OpCodes.Ldc_I4.ToInstruction(newStrings.Count)); // Array size
            il.Add(OpCodes.Newarr.ToInstruction(module.CorLibTypes.String));

            for (int i = 0; i < newStrings.Count; i++)
            {
                il.Add(OpCodes.Dup.ToInstruction()); // Duplicate array reference
                il.Add(OpCodes.Ldc_I4.ToInstruction(i)); // Array index
                il.Add(OpCodes.Ldstr.ToInstruction(newStrings[i])); // String value
                il.Add(OpCodes.Stelem_Ref.ToInstruction()); // Store element
            }

            // Store in static field
            il.Add(OpCodes.Stsfld.ToInstruction(field));

            il.Add(OpCodes.Ret.ToInstruction());

            //Write to file to set new metadata offsets
            string tempPath = pathName + ".strings.replace.tmp.exe";
            try
            {
                var writerOpts = new ModuleWriterOptions(module) { WritePdb = false };
                writerOpts.MetadataOptions.Flags |= dnlib.DotNet.Writer.MetadataFlags.KeepOldMaxStack;
                module.Write(tempPath, writerOpts);
                File.Delete(pathName);
                File.Move(tempPath, pathName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("String injecting caused error:\n" + ex.ToString());
            }

            module = ModuleDefMD.Load(pathName);

            // Find the target type
            targetType = module.Types.First(t => t.FullName == plStringsType);

            // Find the static field
            field = targetType.Fields.First(f => f.Name == plStringsArrayName);

            // Get the static constructor (.cctor)
            cctor = targetType.FindStaticConstructor();

            //Now that the strings are in the file we have to go back through and replace them because adding them changed metadata offsets in our code
            //It's complicated, but pretty much we have to add them twice.
            int j = 0;
            foreach (var instr in cctor.Body.Instructions)
            {
                if (instr.OpCode == OpCodes.Ldstr)
                {
                    // replace with encrypted value
                    if (useNewNames) instr.Operand = Method_Crypter.EncryptMethod.GetEncryptedMethod(module, pathName, renameList[j].overloadIndex, renameList[j].TypeFullName, renameList[j].NewName, key, iv);
                    else instr.Operand = Method_Crypter.EncryptMethod.GetEncryptedMethod(module, pathName, renameList[j].overloadIndex, renameList[j].TypeFullName, renameList[j].OriginalName, key, iv);
                    j++;
                }
            }
            return module;
        }
    }
}

