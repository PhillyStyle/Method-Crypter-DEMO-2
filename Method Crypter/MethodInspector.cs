using dnlib.DotNet;
using dnlib.DotNet.MD;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

public static class MethodInspector
{
    // Populates a ListView instead of showing a MessageBox
    public static void PopulateListView(string exePath, ListView listView)
    {
        if (exePath == null)
            throw new ArgumentNullException(nameof(exePath));
        if (listView == null)
            throw new ArgumentNullException(nameof(listView));

        var module = ModuleDefMD.Load(exePath);

        listView.BeginUpdate();
        listView.Items.Clear();

        foreach (var type in module.GetTypes()) // includes nested types
        {
            foreach (var method in type.Methods)
            {
                try
                {
                    uint rva = (uint)method.RVA;
                    int encodedSize = GetEncodedMethodSize(module, method);
                    string rvaHex = rva != 0 ? $"0x{rva:X8}" : "0x00000000";
                    string token = method.MDToken.ToString();

                    var item = new ListViewItem(type.FullName);
                    item.SubItems.Add(method.Name);
                    item.SubItems.Add(token);
                    item.SubItems.Add(rvaHex);
                    item.SubItems.Add(encodedSize.ToString());

                    listView.Items.Add(item);
                }
                catch (Exception ex)
                {
                    var item = new ListViewItem(type.FullName);
                    item.SubItems.Add(method.Name);
                    item.SubItems.Add("ERROR");
                    item.SubItems.Add("-");
                    item.SubItems.Add(ex.Message);

                    listView.Items.Add(item);
                }
            }
        }

        listView.EndUpdate();
        AutoResizeColumns(listView);
    }

    // Resizes columns to fit content neatly
    private static void AutoResizeColumns(ListView listView)
    {
        foreach (ColumnHeader column in listView.Columns)
            column.Width = -2; // Auto size to content
    }

    public class AdvancedTokenProvider : ITokenProvider, IWriterError
    {
        private int _nextToken = 1; // Start token from 1
        private readonly Dictionary<object, MDToken> _objectTokens = new Dictionary<object, MDToken>();

        public MDToken GetToken()
        {
            return new MDToken(Table.Module, (uint)_nextToken++);
        }

        public MDToken GetToken(object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            // Check if the object already has a token
            if (_objectTokens.TryGetValue(obj, out MDToken token))
            {
                return token; // Return existing token
            }

            // Generate a new token for the object
            token = GetToken();
            _objectTokens[obj] = token; // Store the token
            return token;
        }

        public MDToken GetToken(IList<TypeSig> typeSigs, uint baseToken)
        {
            // For simplicity, we can just return a new token based on the baseToken
            // In a real implementation, you might want to handle type signatures more carefully
            return new MDToken(Table.TypeDef, baseToken + (uint)typeSigs.Count); // Example logic
        }

        public void FreeToken(MDToken token)
        {
            // In this simple implementation, we won't reuse tokens
        }

        public void Error(string message)
        {
            // Handle the error (e.g., log it or throw an exception)
            Console.WriteLine($"Error: {message}");
        }
    }


    // Same encoded size logic from before
    private static int GetEncodedMethodSize(ModuleDefMD module, MethodDef method)
    {
        if (method == null || !method.HasBody)
            return 0;

        var tokenProvider = new AdvancedTokenProvider(); // your existing one
        var writer = new MethodBodyWriter(tokenProvider, method);
        writer.Write();
        var buf = writer.GetFullMethodBody();
        return buf?.Length ?? 0;
    }
}
