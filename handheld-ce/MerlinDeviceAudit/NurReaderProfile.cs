using System;

using System.Reflection;

using System.Runtime.InteropServices;



namespace MerlinAudit

{

    /// <summary>Best-effort NUR module tuning for lost-item / weak RSSI tracking.</summary>

    internal static class NurReaderProfile

    {

        public static NurProfileStatus ApplyAndVerify(object api)
        {
            return ApplyRfSettings(
                api, NurProfileStatus.TargetLinkFreqHz, NurProfileStatus.TargetTxLevel);
        }

        public static NurProfileStatus ApplyRfSettings(object api, int linkFreqHz, int txLevel)
        {
            var st = new NurProfileStatus();
            if (api == null) return st;

            st.EpcSelectMethodFound = HasInventorySelectByEpc(api);
            st.ApplyCalled = true;

            try
            {
                Type apiType = api.GetType();
                Assembly asm = apiType.Assembly;

                if (TryApplyViaModuleSetup(api, apiType, asm, st, linkFreqHz, txLevel))
                {
                    return st;
                }

                if (TryApplyViaDirectSetup(api, st, linkFreqHz, txLevel))
                {
                    return st;
                }

                int typeCount = NurAssemblyTypes.SafeGetTypes(asm).Length;
                st.ApplyError = "no setup type (" + typeCount + " types in DLL)";
            }
            catch (Exception ex)
            {
                st.ApplyOk = false;
                st.ApplyError = ex.Message;
            }
            return st;
        }



        public static bool TryInventorySelectByEpc(object api, string epcHex)

        {

            if (api == null || epcHex == null || epcHex.Length == 0) return false;

            byte[] epc = HexToBytes(epcHex);

            if (epc == null || epc.Length == 0) return false;



            Type apiType = api.GetType();

            string[] names = new string[] { "InventorySelectByEPC", "InventorySelectByEpc" };

            for (int n = 0; n < names.Length; n++)

            {

                MethodInfo[] methods = apiType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

                for (int i = 0; i < methods.Length; i++)

                {

                    MethodInfo m = methods[i];

                    if (m.Name != names[n]) continue;

                    if (TryInvokeSelect(api, m, epc)) return true;

                }

            }

            return false;

        }



        public static bool HasInventorySelectByEpc(object api)

        {

            if (api == null) return false;

            Type apiType = api.GetType();

            return NurApiReflection.HasMethodNamed(apiType, "InventorySelectByEPC")

                || NurApiReflection.HasMethodNamed(apiType, "InventorySelectByEpc");

        }



        public static NurTagReading[] FilterExactEpc(NurTagReading[] readings, string targetEpc)

        {

            if (readings == null || readings.Length == 0 || targetEpc.Length == 0)

            {

                return new NurTagReading[0];

            }

            string normTarget = RssiTraceRecorder.NormalizeEpc(targetEpc);

            NurTagReading match = null;

            for (int i = 0; i < readings.Length; i++)

            {

                if (readings[i] == null) continue;

                string e = RssiTraceRecorder.NormalizeEpc(readings[i].Epc);

                if (e != normTarget) continue;

                match = readings[i];

            }

            if (match == null) return new NurTagReading[0];

            return new NurTagReading[] { match };

        }



        private static bool TryApplyViaModuleSetup(
            object api, Type apiType, Assembly asm, NurProfileStatus st,
            int linkFreqHz, int txLevel)
        {
            Type setupType = FindSetupType(asm, apiType, api);
            if (setupType == null) return false;

            object setup = CreateSetupInstance(setupType);
            if (setup == null)
            {
                st.ApplyError = "cannot create setup";
                return true;
            }

            SetIntField(setup, setupType, "linkFreq", linkFreqHz);
            SetIntField(setup, setupType, "LinkFreq", linkFreqHz);
            SetIntField(setup, setupType, "link_freq", linkFreqHz);
            SetIntField(setup, setupType, "txLevel", txLevel);
            SetIntField(setup, setupType, "TxLevel", txLevel);
            SetIntField(setup, setupType, "tx_level", txLevel);



            int flags = ResolveSetupFlags(asm, setupType);

            if (flags == 0) flags = 0x00000003;



            MethodInfo set = ResolveSetModuleSetup(apiType, setupType);

            if (set == null)

            {

                st.ApplyError = "SetModuleSetup missing";

                return true;

            }



            InvokeSetModuleSetup(api, set, setupType, setup, flags);
            st.ApplyOk = true;
            ReadBackSetup(api, apiType, setupType, st, linkFreqHz, txLevel);
            return true;
        }

        private static bool TryApplyViaDirectSetup(
            object api, NurProfileStatus st, int linkFreqHz, int txLevel)
        {

            string[] setLink = new string[]

            {

                "SetSetupLinkFreq", "SetLinkFreq", "SetLinkFrequency",

            };

            string[] setTx = new string[]

            {

                "SetSetupTxLevel", "SetTxLevel", "SetTxAttenuation",

            };



            bool linkOk = false;

            bool txOk = false;

            for (int i = 0; i < setLink.Length; i++)

            {

                if (NurApiReflection.TryInvokeIntMethod(api, setLink[i], linkFreqHz))
                {
                    linkOk = true;
                    break;
                }
            }
            for (int i = 0; i < setTx.Length; i++)
            {
                if (NurApiReflection.TryInvokeIntMethod(api, setTx[i], txLevel))
                {
                    txOk = true;
                    break;
                }
            }



            if (!linkOk && !txOk) return false;



            st.ApplyOk = linkOk && txOk;

            if (!st.ApplyOk)

            {

                st.ApplyError = linkOk ? "TX setter missing" : "link freq setter missing";

            }



            ReadBackDirectSetup(api, st, linkFreqHz, txLevel);
            if (st.ApplyOk && st.LinkFreqOk && st.TxLevelOk)
            {
                st.ApplyError = "";
            }
            return true;
        }

        private static void ReadBackDirectSetup(
            object api, NurProfileStatus st, int linkFreqHz, int txLevel)
        {

            string[] getLink = new string[]

            {

                "GetSetupLinkFreq", "GetLinkFreq", "GetLinkFrequency",

                "SetupLinkFreq", "LinkFreq", "linkFreq",

            };

            string[] getTx = new string[]

            {

                "GetSetupTxLevel", "GetTxLevel", "SetupTxLevel", "TxLevel", "txLevel",

            };



            int lf;

            if (TryReadIntFromApi(api, getLink, out lf))

            {

                st.ReadLinkFreqHz = lf;
                st.LinkFreqOk = lf == linkFreqHz;
            }
            int tx;
            if (TryReadIntFromApi(api, getTx, out tx))
            {
                st.ReadTxLevel = tx;
                st.TxLevelOk = tx == txLevel;
            }



            if (st.ApplyOk && (!st.LinkFreqOk || !st.TxLevelOk) && st.ApplyError.Length == 0)

            {

                st.ApplyOk = true;

            }

        }



        private static bool TryReadIntFromApi(object api, string[] names, out int value)

        {

            value = -1;

            if (api == null) return false;

            Type apiType = api.GetType();

            for (int i = 0; i < names.Length; i++)

            {

                MethodInfo getM = NurApiReflection.FindInstanceMethod(apiType, names[i], 0);

                if (getM != null && getM.ReturnType == typeof(int))

                {

                    try

                    {

                        value = (int)getM.Invoke(api, null);

                        return true;

                    }

                    catch { }

                }

            }

            return NurApiReflection.TryReadIntMember(api, names, out value);

        }



        private static void ReadBackSetup(
            object api, Type apiType, Type setupType, NurProfileStatus st,
            int linkFreqHz, int txLevel)

        {

            int flags = ResolveSetupFlags(apiType.Assembly, setupType);

            if (flags == 0) flags = 0x00000003;



            object setup = TryGetModuleSetupObject(api, apiType, setupType, flags);

            if (setup == null)

            {

                MethodInfo get = ResolveGetModuleSetup(apiType, setupType);

                if (get == null)

                {

                    st.ApplyError = "GetModuleSetup missing";

                    st.ApplyOk = false;

                    return;

                }



                setup = CreateSetupInstance(setupType);

                if (setup == null)

                {

                    st.ApplyError = "cannot create setup";

                    st.ApplyOk = false;

                    return;

                }

                InvokeGetModuleSetup(api, get, setupType, setup, flags);

            }



            st.ReadLinkFreqHz = ReadIntField(setup, setupType, "linkFreq", "LinkFreq", "link_freq");

            st.ReadTxLevel = ReadIntField(setup, setupType, "txLevel", "TxLevel", "tx_level");

            st.LinkFreqOk = st.ReadLinkFreqHz == linkFreqHz;
            st.TxLevelOk = st.ReadTxLevel == txLevel;
        }



        private static Type FindSetupType(Assembly asm, Type apiType, object api)

        {

            string[] names = new string[]

            {

                "NurApiDotNet.NurModuleSetup",

                "NurApi.NurModuleSetup",

                "NurApiDotNet.NUR_MODULESETUP",

                "NurApi.NUR_MODULESETUP",

                "NurModuleSetup",

                "NUR_MODULESETUP",

            };

            for (int i = 0; i < names.Length; i++)

            {

                Type t = asm.GetType(names[i], false);

                if (IsSetupLikeType(t)) return t;

            }



            Type fromMethods = FindSetupTypeFromMethods(apiType);

            if (fromMethods != null) return fromMethods;



            Type[] nested = apiType.GetNestedTypes(

                BindingFlags.Public | BindingFlags.NonPublic);

            for (int i = 0; i < nested.Length; i++)

            {

                if (IsSetupLikeType(nested[i])) return nested[i];

            }



            Type[] types = NurAssemblyTypes.SafeGetTypes(asm);

            for (int i = 0; i < types.Length; i++)

            {

                Type t = types[i];

                if (!IsSetupLikeType(t)) continue;

                if (t.Name == "NurModuleSetup" || t.Name == "NUR_MODULESETUP") return t;

            }

            for (int i = 0; i < types.Length; i++)

            {

                if (IsSetupLikeType(types[i])) return types[i];

            }



            object sample = TryGetModuleSetupObject(api, apiType, null, 0x00000003);

            if (sample != null) return sample.GetType();



            return null;

        }



        private static Type FindSetupTypeFromMethods(Type apiType)

        {

            string[] methodNames = new string[] { "GetModuleSetup", "SetModuleSetup" };

            for (int m = 0; m < methodNames.Length; m++)

            {

                MethodInfo[] methods = apiType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

                for (int i = 0; i < methods.Length; i++)

                {

                    if (methods[i].Name != methodNames[m]) continue;

                    Type t = ExtractSetupTypeFromMethod(methods[i]);

                    if (t != null) return t;

                }

            }

            return null;

        }



        private static Type ExtractSetupTypeFromMethod(MethodInfo method)

        {

            if (method == null) return null;

            if (IsSetupLikeType(method.ReturnType)) return method.ReturnType;



            ParameterInfo[] ps = method.GetParameters();

            if (ps == null) return null;

            for (int i = 0; i < ps.Length; i++)

            {

                if (IsSetupLikeType(ps[i].ParameterType)) return ps[i].ParameterType;

            }

            return null;

        }



        private static bool IsSetupLikeType(Type t)

        {

            if (t == null || t.IsEnum || t == typeof(void)) return false;

            string n = t.Name.ToUpper();

            if (n.IndexOf("MODULESETUP") >= 0) return true;

            return HasLinkFreqMember(t);

        }



        private static bool HasLinkFreqMember(Type t)

        {

            if (t.GetField("linkFreq") != null) return true;

            if (t.GetField("LinkFreq") != null) return true;

            if (t.GetProperty("linkFreq") != null) return true;

            if (t.GetProperty("LinkFreq") != null) return true;

            return false;

        }



        private static object CreateSetupInstance(Type setupType)

        {

            try

            {

                if (setupType.IsValueType) return Activator.CreateInstance(setupType);

                return Activator.CreateInstance(setupType);

            }

            catch { }

            return null;

        }



        private static object TryGetModuleSetupObject(

            object api, Type apiType, Type setupType, int flags)

        {

            MethodInfo get1 = NurApiReflection.FindInstanceMethod(apiType, "GetModuleSetup", 1);

            if (get1 != null && get1.ReturnType != typeof(void) && get1.ReturnType != typeof(int))

            {

                try

                {

                    object r = get1.Invoke(api, new object[] { flags });

                    if (r != null) return r;

                }

                catch { }

            }



            MethodInfo get0 = NurApiReflection.FindInstanceMethod(apiType, "GetModuleSetup", 0);

            if (get0 != null && get0.ReturnType != typeof(void) && get0.ReturnType != typeof(int))

            {

                try

                {

                    object r = get0.Invoke(api, null);

                    if (r != null) return r;

                }

                catch { }

            }



            if (setupType == null) return null;

            MethodInfo get2 = ResolveGetModuleSetup(apiType, setupType);

            if (get2 == null) return null;



            object setup = CreateSetupInstance(setupType);

            if (setup == null) return null;

            try

            {

                InvokeGetModuleSetup(api, get2, setupType, setup, flags);

                return setup;

            }

            catch { }

            return null;

        }



        private static MethodInfo ResolveSetModuleSetup(Type apiType, Type setupType)

        {

            MethodInfo set = FindSetGetWithSetupType(apiType, "SetModuleSetup", setupType);

            if (set != null) return set;

            return NurApiReflection.ResolveInstanceMethod(

                apiType, "SetModuleSetup", new object[] { 0, null });

        }



        private static MethodInfo ResolveGetModuleSetup(Type apiType, Type setupType)

        {

            MethodInfo get = FindSetGetWithSetupType(apiType, "GetModuleSetup", setupType);

            if (get != null) return get;

            return NurApiReflection.ResolveInstanceMethod(

                apiType, "GetModuleSetup", new object[] { 0, null });

        }



        private static MethodInfo FindSetGetWithSetupType(Type apiType, string name, Type setupType)

        {

            MethodInfo[] methods = apiType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < methods.Length; i++)

            {

                MethodInfo m = methods[i];

                if (m.Name != name) continue;

                ParameterInfo[] ps = m.GetParameters();

                if (ps == null) continue;

                for (int p = 0; p < ps.Length; p++)

                {

                    if (ps[p].ParameterType == setupType) return m;

                }

                if (m.ReturnType == setupType) return m;

            }

            return null;

        }



        private static void InvokeSetModuleSetup(

            object api, MethodInfo set, Type setupType, object setup, int flags)

        {

            ParameterInfo[] ps = set.GetParameters();

            if (ps == null) return;



            if (ps.Length == 3)

            {

                int size = TrySetupSize(setup, setupType);

                if (ps[0].ParameterType == typeof(int))

                {

                    set.Invoke(api, new object[] { flags, setup, size });

                }

                else

                {

                    set.Invoke(api, new object[] { setup, flags, size });

                }

                return;

            }



            if (ps.Length == 2)

            {

                if (ps[0].ParameterType == setupType)

                {

                    set.Invoke(api, new object[] { setup, flags });

                }

                else if (ps[1].ParameterType == setupType)

                {

                    set.Invoke(api, new object[] { flags, setup });

                }

                else if (ps[0].ParameterType == typeof(int))

                {

                    set.Invoke(api, new object[] { flags, setup });

                }

                else

                {

                    set.Invoke(api, new object[] { setup, flags });

                }

                return;

            }



            if (ps.Length == 1 && ps[0].ParameterType == setupType)

            {

                set.Invoke(api, new object[] { setup });

            }

        }



        private static void InvokeGetModuleSetup(

            object api, MethodInfo get, Type setupType, object setup, int flags)

        {

            ParameterInfo[] ps = get.GetParameters();

            if (ps == null) return;



            if (ps.Length == 2)

            {

                if (ps[0].ParameterType == setupType)

                {

                    get.Invoke(api, new object[] { setup, flags });

                }

                else if (ps[1].ParameterType == setupType)

                {

                    get.Invoke(api, new object[] { flags, setup });

                }

                else if (ps[0].ParameterType == typeof(int))

                {

                    get.Invoke(api, new object[] { flags, setup });

                }

                else

                {

                    get.Invoke(api, new object[] { setup, flags });

                }

                return;

            }



            if (ps.Length == 1 && ps[0].ParameterType == typeof(int))

            {

                get.Invoke(api, new object[] { flags });

            }

        }



        private static int TrySetupSize(object setup, Type setupType)

        {

            try

            {

                if (setup != null) return Marshal.SizeOf(setup);

            }

            catch { }

            try

            {

                if (setupType != null && setupType.IsValueType)

                {

                    return Marshal.SizeOf(setupType);

                }

            }

            catch { }

            return 4096;

        }



        private static bool TryInvokeSelect(object api, MethodInfo m, byte[] epc)

        {

            try

            {

                ParameterInfo[] ps = m.GetParameters();

                if (ps == null) return false;



                if (ps.Length == 7)

                {

                    m.Invoke(api, new object[] { 0, 0, 0, false, epc, epc.Length, null });

                    return true;

                }

                if (ps.Length == 6)

                {

                    m.Invoke(api, new object[] { 0, 0, 0, false, epc, epc.Length });

                    return true;

                }

                if (ps.Length == 5)

                {

                    m.Invoke(api, new object[] { 0, 0, 0, false, epc });

                    return true;

                }

            }

            catch { }

            return false;

        }



        private static int ResolveSetupFlags(Assembly asm, Type setupType)

        {

            int v = ReadEnumFlagFromAssembly(asm, "NUR_SETUP_LINKFREQ");

            v |= ReadEnumFlagFromAssembly(asm, "NUR_SETUP_TXLEVEL");

            if (v != 0) return v;



            v = ReadEnumFlag(setupType, "NUR_SETUP_LINKFREQ");

            v |= ReadEnumFlag(setupType, "NUR_SETUP_TXLEVEL");

            return v;

        }



        private static int ReadEnumFlagFromAssembly(Assembly asm, string name)

        {

            string[] typeNames = new string[]

            {

                "NurApiDotNet.NUR_MODULESETUP_FLAGS",

                "NurApi.NUR_MODULESETUP_FLAGS",

                "NUR_MODULESETUP_FLAGS",

            };

            for (int t = 0; t < typeNames.Length; t++)

            {

                Type flagsType = asm.GetType(typeNames[t], false);

                if (flagsType == null) continue;

                int v = ReadEnumFlag(flagsType, name);

                if (v != 0) return v;

            }



            Type[] types = NurAssemblyTypes.SafeGetTypes(asm);

            for (int i = 0; i < types.Length; i++)

            {

                if (types[i] == null || !types[i].IsEnum) continue;

                if (types[i].Name.IndexOf("MODULESETUP") < 0) continue;

                int v = ReadEnumFlag(types[i], name);

                if (v != 0) return v;

            }

            return 0;

        }



        private static int ReadEnumFlag(Type enumType, string name)

        {

            try

            {

                FieldInfo f = enumType.GetField(name);

                if (f != null) return (int)f.GetValue(null);

            }

            catch { }

            return 0;

        }



        private static void SetIntField(object setup, Type setupType, string name, int value)

        {

            FieldInfo f = setupType.GetField(name);

            if (f != null && f.FieldType == typeof(int)) f.SetValue(setup, value);

            PropertyInfo p = setupType.GetProperty(name);

            if (p != null && p.PropertyType == typeof(int) && p.CanWrite)

            {

                p.SetValue(setup, value, null);

            }

        }



        private static int ReadIntField(

            object setup, Type setupType, string name1, string name2, string name3)

        {

            FieldInfo f = setupType.GetField(name1);

            if (f == null) f = setupType.GetField(name2);

            if (f == null) f = setupType.GetField(name3);

            if (f != null && f.FieldType == typeof(int))

            {

                return (int)f.GetValue(setup);

            }

            PropertyInfo p = setupType.GetProperty(name1);

            if (p == null) p = setupType.GetProperty(name2);

            if (p == null) p = setupType.GetProperty(name3);

            if (p != null && p.PropertyType == typeof(int))

            {

                return (int)p.GetValue(setup, null);

            }

            return -1;

        }



        private static byte[] HexToBytes(string hex)

        {

            hex = RssiTraceRecorder.NormalizeEpc(hex);

            if (hex.Length == 0 || (hex.Length & 1) == 1) return null;

            byte[] bytes = new byte[hex.Length / 2];

            for (int i = 0; i < bytes.Length; i++)

            {

                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            }

            return bytes;

        }

    }

}


