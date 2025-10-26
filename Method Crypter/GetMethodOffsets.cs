using dnlib.DotNet;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MethodOffsets
{
    internal class MethodOffsets
    {
        public static void GetMethodOffsets(string exePath, List<MethodRenamer.MethodReplacement> renameList, bool useNewNames)
        {
            ModuleDefMD module = ModuleDefMD.Load(exePath);

            foreach (var mr in renameList)
            {
                if (useNewNames) (mr.ILOffsetFile, mr.ILOffsetVirt, mr.ILSize, mr.IsConstructor) = Method_Crypter.EncryptMethod.GetMethodILOffset(module, mr.overloadIndex, mr.TypeFullName, mr.NewName);
                else (mr.ILOffsetFile, mr.ILOffsetVirt, mr.ILSize, mr.IsConstructor) = Method_Crypter.EncryptMethod.GetMethodILOffset(module, mr.overloadIndex, mr.TypeFullName, mr.OriginalName);
            }

            module.Dispose();
        }
    }
}
