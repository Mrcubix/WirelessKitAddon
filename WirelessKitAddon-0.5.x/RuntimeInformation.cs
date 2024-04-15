using System;
using System.Runtime.InteropServices;

namespace WirelessKitAddon
{
    public class RuntimeInformation
    {
        public static string PlatformCode => GetPlatformCode();

        public static string ArchitectureCode => GetArchitectureCode();

        public static string RuntimeIdentifier => $"{PlatformCode}-{ArchitectureCode}";

        public static string ExecutableExtension => GetExecutableExtension();

        private static string GetPlatformCode()
        {
            if (OperatingSystem.IsWindows())
                return "win";
            if (OperatingSystem.IsLinux())
                return "linux";
            if (OperatingSystem.IsMacOS())
                return "osx";
            return string.Empty;
        }

        private static string GetArchitectureCode()
        {
            // Check if x86 based, arm, ...
            var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        
            return arch switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                _ => string.Empty
            };
        }
        
        private static string GetExecutableExtension()
            => OperatingSystem.IsWindows() ? ".exe" : string.Empty;
    }
}