using System;
using System.Reflection;

namespace MerlinHandheld
{
    internal static class NurApiReflection
    {
        public static bool IsManagedNurDllFile(string fileName)
        {
            if (fileName == null) return false;
            return fileName.ToLower().IndexOf("nurapidotnet") >= 0;
        }

        public static Type ResolveApiType(Assembly asm)
        {
            if (asm == null) return null;
            string[] names = new string[] { "NurApiDotNet.NurApi", "NurApi.NurApi", "NurApi" };
            for (int i = 0; i < names.Length; i++)
            {
                Type t = asm.GetType(names[i], false);
                if (IsApiType(t)) return t;
            }
            try
            {
                Type[] types = asm.GetTypes();
                for (int i = 0; i < types.Length; i++)
                {
                    if (IsApiType(types[i])) return types[i];
                }
            }
            catch { }
            return null;
        }

        public static bool AssemblyHasApiType(Assembly asm)
        {
            return ResolveApiType(asm) != null;
        }

        private static bool IsApiType(Type t)
        {
            if (t == null || !t.IsClass || t.IsAbstract) return false;
            if (t.Name != "NurApi") return false;
            if (HasMethodNamed(t, "ConnectIntegratedReader")) return true;
            if (HasMethodNamed(t, "StartInventoryStream")) return true;
            if (HasMethodNamed(t, "Connect") && HasMethodNamed(t, "StartInventory")) return true;
            return false;
        }

        public static bool HasMethodNamed(Type t, string name)
        {
            MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name) return true;
            }
            return false;
        }
    }
}
