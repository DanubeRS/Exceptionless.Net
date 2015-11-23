using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Exceptionless.Logging;
using Exceptionless.Models.Data;

namespace Exceptionless.Services
{
    public class EnvironmentInfoCollector : IEnvironmentInfoCollector
    {
        private static EnvironmentInfo _environmentInfo;
        private readonly IExceptionlessLog _log;

        public EnvironmentInfoCollector(IExceptionlessLog log)
        {
            _log = log;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }


        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public EnvironmentInfo GetEnvironmentInfo() {
            if (_environmentInfo != null)
                return _environmentInfo;

            var info = new EnvironmentInfo();

            try {
                var loadedManagement = Assembly.Load(new AssemblyName("System.Management, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"));
                var managementObjectSearcher = loadedManagement.GetTypes().First(t => t.Name == "ManagementObjectSearcher");
                var managementObject = Activator.CreateInstance(managementObjectSearcher, "SELECT * FROM Win32_OperatingSystem");
                var managementObjectGet = managementObjectSearcher.InvokeMember("Get", BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod, null, managementObject, null);
                var managementObjectCollectionType = loadedManagement.GetTypes().First(t => t.Name == "ManagementObjectCollection");
                var managementType = loadedManagement.GetTypes().First(t => t.Name == "ManagementObject");
                var managementGenericMethod = ((IEnumerable)managementObjectGet).Cast<object>();
                var @object = managementGenericMethod.First();
                if (@object != null) {
                    info.OSName = (string)managementType.GetMethods().First(m => m.Name == "GetPropertyValue").Invoke(@object, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod, null, new[]{"Caption"}, null);
                    info.OSVersion = Environment.OSVersion.VersionString;
                }

            } catch (Exception e) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Could not locate System.Management dll. Reverting to environment for machine OS info.");
                info.OSName = Environment.OSVersion.VersionString;
                info.OSVersion = info.OSName;
            }

            try {
                if (IsUnix) {
                    if (PerformanceCounterCategory.Exists("Mono Memory")) {
                        var totalPhysicalMemory = new PerformanceCounter("Mono Memory", "Total Physical Memory");
                        info.TotalPhysicalMemory = Convert.ToInt64(totalPhysicalMemory.RawValue);

                        var availablePhysicalMemory = new PerformanceCounter("Mono Memory", "Available Physical Memory"); //mono 4.0+
                        info.AvailablePhysicalMemory = Convert.ToInt64(availablePhysicalMemory.RawValue);
                    }
                } else {
                    var statEX = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(statEX)) {

                        info.TotalPhysicalMemory = Convert.ToInt64(statEX.ullTotalPhys);
                        info.AvailablePhysicalMemory = Convert.ToInt64(statEX.ullAvailPhys);
                    }
                }
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get physical memory. Error message: {0}", ex.Message);
            }

            try {
                info.ProcessorCount = Environment.ProcessorCount;
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get processor count. Error message: {0}", ex.Message);
            }

            try {
                info.MachineName = Environment.MachineName;
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get machine name. Error message: {0}", ex.Message);
            }

            try {
                IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                if (hostEntry != null && hostEntry.AddressList.Any())
                    info.IpAddress = String.Join(", ", hostEntry.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray());
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get ip address. Error message: {0}", ex.Message);
            }

            try {
                Process proc = Process.GetCurrentProcess();
                info.ProcessMemorySize = proc.PrivateMemorySize64;
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get process memory size. Error message: {0}", ex.Message);
            }

            try {
                info.CommandLine = Environment.CommandLine;
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get command line. Error message: {0}", ex.Message);
            }

            try {
                if (IsUnix) {
                    var currentProcess = Process.GetCurrentProcess();
                    info.ProcessId = currentProcess.Id.ToString(NumberFormatInfo.InvariantInfo);
                } else {
                    info.ProcessId = KernelNativeMethods.GetCurrentProcessId().ToString(NumberFormatInfo.InvariantInfo);
                }
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get process id. Error message: {0}", ex.Message);
            }

            try {
                if (IsUnix) {
                    var currentProcess = Process.GetCurrentProcess();
                    info.ProcessName = currentProcess.ProcessName;
                } else {
                    info.ProcessName = GetProcessName();
                }
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get process name. Error message: {0}", ex.Message);
            }

            try {
                if (IsUnix)
                    info.ThreadId = Thread.CurrentThread.ManagedThreadId.ToString(NumberFormatInfo.InvariantInfo);
                else
                    info.ThreadId = KernelNativeMethods.GetCurrentThreadId().ToString(NumberFormatInfo.InvariantInfo);
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get thread id. Error message: {0}", ex.Message);
            }

            try {
                info.Architecture = Is64BitOperatingSystem() ? "x64" : "x86";
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get CPU architecture. Error message: {0}", ex.Message);
            }

            try {
                info.RuntimeVersion = Environment.Version.ToString();
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get CLR version. Error message: {0}", ex.Message);
            }

            try {
                info.Data.Add("AppDomainName", AppDomain.CurrentDomain.FriendlyName);
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get AppDomain friendly name. Error message: {0}", ex.Message);
            }

            try {
                info.ThreadName = Thread.CurrentThread.Name;
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(EnvironmentInfoCollector), "Unable to get current thread name. Error message: {0}", ex.Message);
            }

            _environmentInfo = info;
            return _environmentInfo;
        }

        private static string GetProcessName()
        {
            var buffer = new StringBuilder(1024);
            int length = KernelNativeMethods.GetModuleFileName(KernelNativeMethods.GetModuleHandle(null), buffer, buffer.Capacity);
            return buffer.ToString();
        }

        private static bool Is64BitOperatingSystem()
        {
            if (IntPtr.Size == 8) // 64-bit programs run only on Win64
                return true;

            // Detect whether the current process is a 32-bit process running on a 64-bit system.
            bool is64;
            bool methodExist = KernelNativeMethods.MethodExists("kernel32.dll", "IsWow64Process");

            return ((methodExist && KernelNativeMethods.IsWow64Process(KernelNativeMethods.GetCurrentProcess(), out is64)) && is64);
        }

        private static bool IsUnix
        {
            get
            {
                int platform = (int)Environment.OSVersion.Platform;
                return platform == 4 || platform == 6 || platform == 128;
            }
        }
    }

    internal static class KernelNativeMethods
    {
        #region Kernel32

        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentProcessId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [PreserveSig]
        public static extern int GetModuleFileName([In] IntPtr hModule, [Out] StringBuilder lpFilename, [In] [MarshalAs(UnmanagedType.U4)] int nSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentThreadId();

        #endregion

        public static bool MethodExists(string moduleName, string methodName)
        {
            IntPtr moduleHandle = GetModuleHandle(moduleName);
            if (moduleHandle == IntPtr.Zero)
                return false;

            return (GetProcAddress(moduleHandle, methodName) != IntPtr.Zero);
        }
    }


}