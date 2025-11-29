using System;
using System.Collections.Generic;
using Microsoft.Win32;        //RegistryKey
#if !CONSOLE
using System.Windows.Forms;
#endif
using System.IO;              //Path

namespace JoinFS
{
#if !CONSOLE
    /// <summary>Additional functions for stored settings.</summary>
    public static class OldSettings
    {
        static Dictionary<string, int> intCache = new Dictionary<string, int>();
        static Dictionary<string, string> stringCache = new Dictionary<string, string>();

        static string st_sRegKey;
        static string RegKey()
        {
            if (String.IsNullOrEmpty(st_sRegKey))
            {
                st_sRegKey = Path.Combine(Path.Combine("SOFTWARE", "JoinFS"), "JoinFS");
            }
            return st_sRegKey;
        }

        /// <summary>Write a string value to registry storage.</summary>
        public static void WriteString(string sKey, string sValue)
        {
            // update cache
            stringCache[sKey] = sValue;

            try
            {
                RegistryKey rk = Registry.CurrentUser;
                if (rk != null)
                {
                    RegistryKey rkVal = rk.CreateSubKey(RegKey());
                    if (rkVal != null)
                    {
                        rkVal.SetValue(sKey, sValue == null ? "" : sValue, RegistryValueKind.String);
                        rkVal.Close();
                    }
                    rk.Close();
                }
            }
            catch
            {
            }
        }
        /// <summary>Write an integer value to registry storage.</summary>
        public static void WriteInt32(string sKey, int nValue)
        {
            // update cache
            intCache[sKey] = nValue;

            try
            {
                RegistryKey rk = Registry.CurrentUser;
                if (rk != null)
                {
                    RegistryKey rkVal = rk.CreateSubKey(RegKey());
                    if (rkVal != null)
                    {
                        rkVal.SetValue(sKey, nValue, RegistryValueKind.DWord);
                        rkVal.Close();
                    }
                    rk.Close();
                }
            }
            catch
            {
            }
        }
        /// <summary>Read a string value from registry storage.</summary>
        public static string ReadString(string sKey, int maxLength)
        {
            string result = ReadString(sKey, "");
            return (result.Length > maxLength) ? result.Substring(0, maxLength) : result;
        }
        /// <summary>Read a string value from registry storage.</summary>
        public static string ReadString(string sKey)
        {
            return ReadString(sKey, "");
        }
        /// <summary>Read a string value from registry storage (use default if not found).</summary>
        public static string ReadString(string sKey, string sDefault)
        {
            // check cache
            if (stringCache.ContainsKey(sKey))
            {
                // returned cache version
                return stringCache[sKey];
            }

            string sRet = sDefault;

            try
            {
                RegistryKey rk = Registry.CurrentUser;
                if (rk != null)
                {
                    RegistryKey rkVal = rk.OpenSubKey(RegKey());
                    if (rkVal != null)
                    {
                        sRet = rkVal.GetValue(sKey, sDefault) as string;
                        rkVal.Close();
                    }
                    rk.Close();
                }
            }
            catch
            {
            }

            // save to cache
            stringCache[sKey] = sRet;

            return sRet;
        }
        /// <summary>Read an integer value from registry storage.</summary>
        public static int ReadInt32(string sKey)
        {
            return ReadInt32(sKey, 0);
        }
        /// <summary>Read an integer value from registry storage (use default if not found).</summary>
        public static int ReadInt32(string sKey, int nDefault)
        {
            // check cache
            if (intCache.ContainsKey(sKey))
            {
                // returned cache version
                return intCache[sKey];
            }

            int nRet = nDefault;

            try
            {
                RegistryKey rk = Registry.CurrentUser;
                if (rk != null)
                {
                    RegistryKey rkVal = rk.OpenSubKey(RegKey());
                    if (rkVal != null)
                    {
                        nRet = Convert.ToInt32(rkVal.GetValue(sKey, nDefault));
                        rkVal.Close();
                    }
                    rk.Close();
                }
            }
            catch
            {
            }

            // save to cache
            intCache[sKey] = nRet;

            return nRet;
        }

        /// <summary>Delete a string value to registry storage.</summary>
        public static void Delete(string sKey)
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser;
                if (rk != null)
                {
                    RegistryKey rkVal = rk.CreateSubKey(RegKey());
                    if (rkVal != null)
                    {
                        rkVal.DeleteSubKey(sKey);
                        rkVal.Close();
                    }
                    rk.Close();
                }
            }
            catch
            {
            }
        }
    }
#endif
}

