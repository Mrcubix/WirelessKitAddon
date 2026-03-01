using System;
using System.Runtime.InteropServices;

namespace WirelessKitAddon.Native
{
    /// <summary>
    ///   P/Invoke declarations for macOS IOKit HID and CoreFoundation APIs.
    ///   Used to read battery reports from the Wacom wireless dongle on macOS,
    ///   where HidSharp does not expose the 32-byte input endpoint.
    /// </summary>
    internal static class IOKitInterop
    {
        private const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";
        private const string CFLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string LibSystem = "/usr/lib/libSystem.dylib";

        #region CoreFoundation

        [DllImport(LibSystem)]
        public static extern IntPtr dlopen(string path, int mode);

        [DllImport(LibSystem)]
        public static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport(CFLib)]
        public static extern IntPtr CFDictionaryCreateMutable(
            IntPtr allocator, long capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

        [DllImport(CFLib)]
        public static extern void CFDictionarySetValue(IntPtr dict, IntPtr key, IntPtr value);

        [DllImport(CFLib)]
        public static extern IntPtr CFNumberCreate(IntPtr allocator, CFNumberType type, ref int value);

        [DllImport(CFLib)]
        public static extern bool CFNumberGetValue(IntPtr number, CFNumberType type, out int value);

        [DllImport(CFLib)]
        public static extern IntPtr CFSetGetValues(IntPtr set, IntPtr[] values);

        [DllImport(CFLib)]
        public static extern long CFSetGetCount(IntPtr set);

        [DllImport(CFLib)]
        public static extern void CFRelease(IntPtr cf);

        [DllImport(CFLib)]
        public static extern IntPtr CFRunLoopGetCurrent();

        [DllImport(CFLib)]
        public static extern void CFRunLoopRun();

        [DllImport(CFLib)]
        public static extern void CFRunLoopStop(IntPtr runLoop);

        [DllImport(CFLib)]
        public static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string str, uint encoding);

        public enum CFNumberType
        {
            kCFNumberIntType = 9
        }

        /// <summary>UTF-8 encoding for CFString creation.</summary>
        public const uint kCFStringEncodingUTF8 = 0x08000100;

        /// <summary>
        ///   Resolves a CoreFoundation global symbol pointer (e.g., kCFTypeDictionaryKeyCallBacks).
        /// </summary>
        public static IntPtr GetCFConstant(string name)
        {
            var handle = dlopen(CFLib, 0);
            var ptr = dlsym(handle, name);
            return ptr != IntPtr.Zero ? Marshal.ReadIntPtr(ptr) : IntPtr.Zero;
        }

        /// <summary>
        ///   Resolves a CoreFoundation global CFString pointer (e.g., kCFRunLoopDefaultMode).
        /// </summary>
        public static IntPtr GetCFStringConstant(string name)
        {
            var handle = dlopen(CFLib, 0);
            var ptr = dlsym(handle, name);
            return ptr != IntPtr.Zero ? Marshal.ReadIntPtr(ptr) : IntPtr.Zero;
        }

        /// <summary>
        ///   Resolves an IOKit global CFString key (e.g., kIOHIDVendorIDKey = "VendorID").
        /// </summary>
        public static IntPtr GetIOKitStringConstant(string name)
        {
            var handle = dlopen(IOKitLib, 0);
            var ptr = dlsym(handle, name);
            return ptr != IntPtr.Zero ? Marshal.ReadIntPtr(ptr) : IntPtr.Zero;
        }

        #endregion

        #region IOKit HID Manager

        [DllImport(IOKitLib)]
        public static extern IntPtr IOHIDManagerCreate(IntPtr allocator, int options);

        [DllImport(IOKitLib)]
        public static extern void IOHIDManagerSetDeviceMatching(IntPtr manager, IntPtr matching);

        [DllImport(IOKitLib)]
        public static extern void IOHIDManagerScheduleWithRunLoop(
            IntPtr manager, IntPtr runLoop, IntPtr runLoopMode);

        [DllImport(IOKitLib)]
        public static extern int IOHIDManagerOpen(IntPtr manager, int options);

        [DllImport(IOKitLib)]
        public static extern IntPtr IOHIDManagerCopyDevices(IntPtr manager);

        [DllImport(IOKitLib)]
        public static extern void IOHIDManagerClose(IntPtr manager, int options);

        [DllImport(IOKitLib)]
        public static extern void IOHIDManagerUnscheduleFromRunLoop(
            IntPtr manager, IntPtr runLoop, IntPtr runLoopMode);

        #endregion

        #region IOKit HID Device

        [DllImport(IOKitLib)]
        public static extern int IOHIDDeviceOpen(IntPtr device, int options);

        [DllImport(IOKitLib)]
        public static extern void IOHIDDeviceClose(IntPtr device, int options);

        [DllImport(IOKitLib)]
        public static extern IntPtr IOHIDDeviceGetProperty(IntPtr device, IntPtr key);

        [DllImport(IOKitLib)]
        public static extern void IOHIDDeviceRegisterInputReportCallback(
            IntPtr device, IntPtr report, long reportLength,
            IOHIDReportCallback callback, IntPtr context);

        [DllImport(IOKitLib)]
        public static extern void IOHIDDeviceScheduleWithRunLoop(
            IntPtr device, IntPtr runLoop, IntPtr runLoopMode);

        public delegate void IOHIDReportCallback(
            IntPtr context, int result, IntPtr sender,
            int type, uint reportID,
            IntPtr report, long reportLength);

        #endregion

        #region IOKit Constants

        public const int kIOReturnSuccess = 0;
        public const int kIOHIDOptionsTypeNone = 0;

        #endregion
    }
}
