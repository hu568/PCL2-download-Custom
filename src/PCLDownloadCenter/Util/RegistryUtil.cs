using System;
using Microsoft.Win32;

namespace PCLDownloadCenter
{
    /// <summary>PCL 的自定义变量存放在 HKCU\Software\PCL\CustomEvent&lt;Name&gt;。</summary>
    public static class RegistryUtil
    {
        public const string PclRegPath = @"Software\PCL";

        public static string ReadVariable(string name, string fallback = null)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PclRegPath, false))
                {
                    if (key == null) return fallback;
                    var value = key.GetValue("CustomEvent" + name) as string;
                    return string.IsNullOrEmpty(value) ? fallback : value;
                }
            }
            catch { return fallback; }
        }

        public static bool WriteVariable(string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PclRegPath))
                {
                    if (key == null) return false;
                    key.SetValue("CustomEvent" + name, value ?? "", RegistryValueKind.String);
                    return true;
                }
            }
            catch { return false; }
        }

        public static string ReadSetting(string name, string fallback = null)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PclRegPath, false))
                {
                    if (key == null) return fallback;
                    var value = key.GetValue(name) as string;
                    return string.IsNullOrEmpty(value) ? fallback : value;
                }
            }
            catch { return fallback; }
        }
    }
}
