using System;

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
            => Environment.Is64BitProcess ? "x64" : "x86";

        private static string GetExecutableExtension()
            => OperatingSystem.IsWindows() ? ".exe" : string.Empty;
    }
}