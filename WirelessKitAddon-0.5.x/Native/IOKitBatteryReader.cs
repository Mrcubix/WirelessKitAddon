using System;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Plugin.Tablet;
using WirelessKitAddon.Interfaces;
using WirelessKitAddon.Reports;
using static WirelessKitAddon.Native.IOKitInterop;

namespace WirelessKitAddon.Native
{
    /// <summary>
    ///   Reads battery reports from the Wacom wireless dongle on macOS using IOKit HID APIs.
    ///
    ///   On macOS, the dongle's HID report descriptor marks Report ID 0x80 as Input (Constant),
    ///   which causes HidSharp to report MaxInputReportLength=0 for the wireless monitor interface.
    ///   However, IOKit's <c>IOHIDDeviceRegisterInputReportCallback</c> still delivers these reports.
    ///   This class directly uses IOKit to receive and parse the 32-byte battery reports.
    /// </summary>
    internal sealed class IOKitBatteryReader : IDisposable
    {
        private const int WACOM_VID = 0x056A;
        private const int WIRELESS_KIT_PID = 0x0084;
        private const int USAGE_PAGE_WIRELESS_MONITOR = 0xFF01;
        private const int USAGE_WIRELESS_MONITOR = 0x80;
        private const int REPORT_BUFFER_SIZE = 64;

        private Thread? _runLoopThread;
        private IntPtr _runLoopRef;
        private IntPtr _runLoopMode;
        private IntPtr _manager;
        private IntPtr _device;
        private GCHandle _reportBufferHandle;
        private IOHIDReportCallback? _callbackDelegate;
        private volatile bool _disposed;

        /// <summary>Raised when a parsed wireless report is received.</summary>
        public event EventHandler<IDeviceReport>? Report;

        /// <summary>Raised when the reading state changes (started/stopped).</summary>
        public event EventHandler<bool>? ReadingChanged;

        /// <summary>Whether the reader is currently receiving reports.</summary>
        public bool IsReading { get; private set; }

        /// <summary>
        ///   Attempts to find and open the Wacom wireless monitor HID service,
        ///   then starts receiving battery reports on a background thread.
        /// </summary>
        /// <returns><c>true</c> if the device was found and opened successfully.</returns>
        public bool Start()
        {
            if (!OperatingSystem.IsMacOS())
                return false;

            _manager = IOHIDManagerCreate(IntPtr.Zero, kIOHIDOptionsTypeNone);
            if (_manager == IntPtr.Zero)
                return false;

            // Build matching dictionary for Wacom wireless receiver
            var kCFTypeDictionaryKeyCallBacks = dlsym(dlopen(
                "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", 0),
                "kCFTypeDictionaryKeyCallBacks");
            var kCFTypeDictionaryValueCallBacks = dlsym(dlopen(
                "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", 0),
                "kCFTypeDictionaryValueCallBacks");

            var match = CFDictionaryCreateMutable(IntPtr.Zero, 0,
                kCFTypeDictionaryKeyCallBacks, kCFTypeDictionaryValueCallBacks);

            SetDictionaryInt(match, "VendorID", WACOM_VID);
            SetDictionaryInt(match, "ProductID", WIRELESS_KIT_PID);

            IOHIDManagerSetDeviceMatching(_manager, match);
            CFRelease(match);

            // We schedule with the current thread's run loop temporarily just to enumerate,
            // then reschedule on the dedicated thread.
            // Actually, we'll do all work on the dedicated thread.

            _runLoopThread = new Thread(RunLoopWorker)
            {
                Name = "IOKitBatteryReader",
                IsBackground = true
            };
            _runLoopThread.Start();

            return true;
        }

        private void RunLoopWorker()
        {
            try
            {
                _runLoopRef = CFRunLoopGetCurrent();

                _runLoopMode = GetCFStringConstant("kCFRunLoopDefaultMode");

                IOHIDManagerScheduleWithRunLoop(_manager, _runLoopRef, _runLoopMode);

                int openResult = IOHIDManagerOpen(_manager, kIOHIDOptionsTypeNone);
                if (openResult != kIOReturnSuccess)
                    return;

                var deviceSet = IOHIDManagerCopyDevices(_manager);
                if (deviceSet == IntPtr.Zero)
                    return;

                long count = CFSetGetCount(deviceSet);
                if (count == 0)
                {
                    CFRelease(deviceSet);
                    return;
                }

                var devices = new IntPtr[count];
                CFSetGetValues(deviceSet, devices);

                // Find the wireless monitor interface (UsagePage 0xFF01, Usage 0x80)
                // Note: kIOHIDPrimaryUsagePageKey / kIOHIDPrimaryUsageKey are C preprocessor
                // macros (#define), NOT exported symbols, so dlsym cannot find them.
                // We must create CFStrings from their literal values instead.
                _device = IntPtr.Zero;
                var usagePageKey = CFStringCreateWithCString(IntPtr.Zero, "PrimaryUsagePage", kCFStringEncodingUTF8);
                var usageKey = CFStringCreateWithCString(IntPtr.Zero, "PrimaryUsage", kCFStringEncodingUTF8);

                for (int i = 0; i < count; i++)
                {
                    int usagePage = GetDeviceIntProperty(devices[i], usagePageKey);
                    int usage = GetDeviceIntProperty(devices[i], usageKey);

                    if (usagePage == USAGE_PAGE_WIRELESS_MONITOR && usage == USAGE_WIRELESS_MONITOR)
                    {
                        _device = devices[i];
                        break;
                    }
                }

                CFRelease(usagePageKey);
                CFRelease(usageKey);
                CFRelease(deviceSet);

                if (_device == IntPtr.Zero)
                    return;

                int deviceOpenResult = IOHIDDeviceOpen(_device, kIOHIDOptionsTypeNone);
                if (deviceOpenResult != kIOReturnSuccess)
                    return;

                // Allocate a pinned report buffer for the callback
                var reportBuffer = new byte[REPORT_BUFFER_SIZE];
                _reportBufferHandle = GCHandle.Alloc(reportBuffer, GCHandleType.Pinned);

                // Keep a strong reference to the delegate to prevent GC collection
                _callbackDelegate = OnInputReport;

                IOHIDDeviceRegisterInputReportCallback(
                    _device,
                    _reportBufferHandle.AddrOfPinnedObject(),
                    REPORT_BUFFER_SIZE,
                    _callbackDelegate,
                    IntPtr.Zero);

                IsReading = true;
                ReadingChanged?.Invoke(this, true);

                // Block this thread on the run loop — callbacks will fire here
                CFRunLoopRun();
            }
            catch
            {
                // Swallow — the reader is being disposed or the device disconnected
            }
            finally
            {
                IsReading = false;
                ReadingChanged?.Invoke(this, false);
            }
        }

        private void OnInputReport(IntPtr context, int result, IntPtr sender,
            int type, uint reportID, IntPtr reportPtr, long reportLength)
        {
            if (_disposed || reportID != 0x80 || reportLength < 8)
                return;

            // Copy the report data from the native buffer
            var report = new byte[reportLength];
            Marshal.Copy(reportPtr, report, 0, (int)reportLength);

            try
            {
                var parsed = new Wacom32bWirelessReport(report);
                Report?.Invoke(this, parsed);
            }
            catch
            {
                // Don't let subscriber exceptions kill the run loop
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // 1. Unregister the input report callback so IOKit stops delivering reports.
            //    The buffer pointer is still valid here (GCHandle not yet freed).
            if (_device != IntPtr.Zero && _reportBufferHandle.IsAllocated)
            {
                IOHIDDeviceRegisterInputReportCallback(
                    _device,
                    _reportBufferHandle.AddrOfPinnedObject(),
                    REPORT_BUFFER_SIZE,
                    null!,
                    IntPtr.Zero);
            }

            // 2. Unschedule the manager from the run loop so no further events are dispatched
            if (_manager != IntPtr.Zero && _runLoopRef != IntPtr.Zero && _runLoopMode != IntPtr.Zero)
                IOHIDManagerUnscheduleFromRunLoop(_manager, _runLoopRef, _runLoopMode);

            // 3. Stop the run loop (unblocks the background thread)
            if (_runLoopRef != IntPtr.Zero)
                CFRunLoopStop(_runLoopRef);

            // 4. Wait for the run loop thread to exit
            _runLoopThread?.Join(2000);

            // 5. Close the device and manager now that no callbacks can fire
            if (_device != IntPtr.Zero)
            {
                IOHIDDeviceClose(_device, kIOHIDOptionsTypeNone);
                _device = IntPtr.Zero;
            }

            if (_manager != IntPtr.Zero)
            {
                IOHIDManagerClose(_manager, kIOHIDOptionsTypeNone);
                CFRelease(_manager);
                _manager = IntPtr.Zero;
            }

            // 6. Free the pinned buffer last — IOKit no longer references it
            if (_reportBufferHandle.IsAllocated)
                _reportBufferHandle.Free();

            _callbackDelegate = null;
        }

        #region Helpers

        private static void SetDictionaryInt(IntPtr dict, string key, int value)
        {
            var cfKey = CFStringCreateWithCString(IntPtr.Zero, key, kCFStringEncodingUTF8);
            var cfValue = CFNumberCreate(IntPtr.Zero, CFNumberType.kCFNumberIntType, ref value);
            CFDictionarySetValue(dict, cfKey, cfValue);
            CFRelease(cfKey);
            CFRelease(cfValue);
        }

        private static int GetDeviceIntProperty(IntPtr device, IntPtr key)
        {
            var prop = IOHIDDeviceGetProperty(device, key);
            if (prop == IntPtr.Zero)
                return 0;
            CFNumberGetValue(prop, CFNumberType.kCFNumberIntType, out int value);
            return value;
        }

        #endregion
    }
}
