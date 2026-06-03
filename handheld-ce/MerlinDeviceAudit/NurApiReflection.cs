using System;
using System.Reflection;

namespace MerlinAudit
{
    /// <summary>Safe assembly type scan (CE NurApiDotNetWCE often fails bare GetTypes).</summary>
    internal static class NurAssemblyTypes
    {
        public static Type[] SafeGetTypes(Assembly asm)
        {
            if (asm == null) return new Type[0];
            try
            {
                Type[] types = asm.GetTypes();
                if (types != null) return types;
            }
            catch { }
            return new Type[0];
        }
    }

    /// <summary>Resolves NurApiDotNet.NurApi (Windows CE Nordic SDK).</summary>
    internal static class NurApiReflection
    {
        public static string LastResolvedTypeName = "";

        public static bool IsManagedNurDllFile(string fileName)
        {
            if (fileName == null) return false;
            string n = fileName.ToLower();
            return n.IndexOf("nurapidotnet") >= 0;
        }

        public static Type ResolveApiType(Assembly asm)
        {
            LastResolvedTypeName = "";
            if (asm == null) return null;

            string[] names = new string[]
            {
                "NurApiDotNet.NurApi",
                "NurApi.NurApi",
                "NurApi",
            };

            for (int i = 0; i < names.Length; i++)
            {
                Type t = asm.GetType(names[i], false);
                if (IsApiType(t))
                {
                    LastResolvedTypeName = t.FullName;
                    return t;
                }
            }

            Type[] types = NurAssemblyTypes.SafeGetTypes(asm);
            for (int i = 0; i < types.Length; i++)
            {
                if (IsApiType(types[i]))
                {
                    LastResolvedTypeName = types[i].FullName;
                    return types[i];
                }
            }

            return null;
        }

        public static MethodInfo FindInstanceMethod(Type t, string name, int paramCount)
        {
            if (t == null || name == null) return null;
            MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != name) continue;
                ParameterInfo[] ps = m.GetParameters();
                int n = ps == null ? 0 : ps.Length;
                if (n == paramCount) return m;
            }
            return null;
        }

        public static bool TryInvokeIntMethod(object target, string name, int value)
        {
            if (target == null) return false;
            MethodInfo m = FindInstanceMethod(target.GetType(), name, 1);
            if (m == null) return false;
            try
            {
                m.Invoke(target, new object[] { value });
                return true;
            }
            catch { }
            return false;
        }

        public static bool TryReadIntMember(object target, string[] names, out int value)
        {
            value = -1;
            if (target == null || names == null) return false;
            Type t = target.GetType();
            for (int n = 0; n < names.Length; n++)
            {
                try
                {
                    PropertyInfo p = t.GetProperty(names[n]);
                    if (p != null && p.PropertyType == typeof(int))
                    {
                        value = (int)p.GetValue(target, null);
                        return true;
                    }
                    FieldInfo f = t.GetField(names[n]);
                    if (f != null && f.FieldType == typeof(int))
                    {
                        value = (int)f.GetValue(target);
                        return true;
                    }
                }
                catch { }
            }
            return false;
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
            if (HasMethodNamed(t, "StartInventoryStream") && HasMethodNamed(t, "GetTagStorage"))
            {
                return true;
            }
            if (HasMethodNamed(t, "Connect") && HasMethodNamed(t, "StartInventory"))
            {
                return true;
            }
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

        /// <summary>
        /// Pick one overload when GetMethod(name) would throw AmbiguousMatchException on CE.
        /// </summary>
        public static MethodInfo ResolveInstanceMethod(Type t, string name, object[] args)
        {
            if (t == null || name == null) return null;
            int want = args == null ? 0 : args.Length;
            MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo fallback = null;
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != name) continue;
                ParameterInfo[] ps = m.GetParameters();
                int n = ps == null ? 0 : ps.Length;
                if (n != want) continue;
                if (fallback == null) fallback = m;
                if (n == 0) return m;
            }
            return fallback;
        }

        public static bool TryGetBoolProperty(object target, string name, out bool value)
        {
            value = false;
            if (target == null || name == null) return false;
            try
            {
                PropertyInfo p = target.GetType().GetProperty(name);
                if (p == null) return false;
                object v = p.GetValue(target, null);
                if (v is bool) { value = (bool)v; return true; }
            }
            catch { }
            return false;
        }
    }
}
