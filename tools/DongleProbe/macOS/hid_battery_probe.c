/*
 * hid_battery_probe.c — Directly queries Report ID 0x80 via IOKit
 * on the Wacom Wireless Receiver (VID 0x056A, PID 0x0084).
 *
 * The HID descriptor marks this report as Input (Constant), which causes
 * HidSharp and IOKit's callback-based delivery to ignore it. This tool
 * bypasses that by using IOHIDDeviceGetReport(kIOHIDReportTypeInput, 0x80).
 *
 * Build:
 *   clang -framework IOKit -framework CoreFoundation hid_battery_probe.c -o hid_battery_probe
 *
 * Run:
 *   ./hid_battery_probe
 */

#include <IOKit/hid/IOHIDManager.h>
#include <IOKit/hid/IOHIDDevice.h>
#include <CoreFoundation/CoreFoundation.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#define WACOM_VID      0x056A
#define WACOM_PID      0x0084
#define REPORT_ID      0x80
#define REPORT_LEN     32
#define USAGE_PAGE_WL  0xFF01
#define USAGE_WL       0x80

static void try_get_report(IOHIDDeviceRef device, IOHIDReportType type, const char *type_name, uint8_t report_id, CFIndex len)
{
    uint8_t buf[256];
    memset(buf, 0, sizeof(buf));
    CFIndex actual_len = len;

    IOReturn ret = IOHIDDeviceGetReport(device, type, report_id, buf, &actual_len);
    if (ret == kIOReturnSuccess) {
        printf("  %s Report 0x%02X (%lld bytes): ", type_name, report_id, (long long)actual_len);
        for (CFIndex i = 0; i < actual_len && i < 40; i++)
            printf("%02X ", buf[i]);
        if (actual_len > 40)
            printf("...");
        printf("\n");

        // Parse battery from byte 5 (index 5 if report_id is at index 0, or index 4 if report_id is stripped)
        // IOHIDDeviceGetReport does NOT prepend the report ID, so data starts at buf[0]
        // Actually, the behavior varies. Let's check both interpretations:
        if (actual_len >= 6) {
            // If buf[0] is the report ID (0x80):
            if (buf[0] == 0x80 && actual_len >= 6) {
                int connected = buf[1] & 0x01;
                float battery = (buf[5] & 0x3F) * 100.0f / 31.0f;
                int charging = (buf[5] & 0x80) != 0;
                printf("    → [with report ID] Connected=%d, Battery=%.1f%%, Charging=%d\n", connected, battery, charging);
            }
            // If buf[0] is data[1] (report ID was stripped):
            if (actual_len >= 5) {
                int connected = buf[0] & 0x01;
                float battery = (buf[4] & 0x3F) * 100.0f / 31.0f;
                int charging = (buf[4] & 0x80) != 0;
                printf("    → [without report ID] Connected=%d, Battery=%.1f%%, Charging=%d\n", connected, battery, charging);
            }
        }
    } else {
        printf("  %s Report 0x%02X: FAILED (0x%08X)\n", type_name, report_id, ret);
    }
}

static void input_report_callback(void *context, IOReturn result, void *sender,
                                   IOHIDReportType type, uint32_t reportID,
                                   uint8_t *report, CFIndex reportLength)
{
    printf("  Callback: ReportID=0x%02X, Type=%d, Length=%lld: ", reportID, type, (long long)reportLength);
    for (CFIndex i = 0; i < reportLength && i < 40; i++)
        printf("%02X ", report[i]);
    printf("\n");

    // The callback buffer includes the report ID as the first byte.
    // Layout: [0]=ReportID, [1]=connection, [2-4]=?, [5]=battery, [6-7]=PID(BE)
    if (reportID == REPORT_ID && reportLength >= 8) {
        int connected = report[1] & 0x01;
        float battery = (report[5] & 0x3F) * 100.0f / 31.0f;
        int charging = (report[5] & 0x80) != 0;
        int tablet_pid = (report[6] << 8) | report[7];
        printf("    → Connected=%d, Battery=%.1f%%, Charging=%d, TabletPID=0x%04X\n",
               connected, battery, charging, tablet_pid);
    }
}

int main(void)
{
    printf("=== Wacom Wireless Dongle — IOKit Battery Probe ===\n\n");

    IOHIDManagerRef manager = IOHIDManagerCreate(kCFAllocatorDefault, kIOHIDOptionsTypeNone);
    if (!manager) {
        fprintf(stderr, "Failed to create HID manager\n");
        return 1;
    }

    // Match Wacom wireless receiver
    CFMutableDictionaryRef match = CFDictionaryCreateMutable(kCFAllocatorDefault, 0,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);

    int vid = WACOM_VID, pid = WACOM_PID;
    CFNumberRef vid_cf = CFNumberCreate(kCFAllocatorDefault, kCFNumberIntType, &vid);
    CFNumberRef pid_cf = CFNumberCreate(kCFAllocatorDefault, kCFNumberIntType, &pid);
    CFDictionarySetValue(match, CFSTR(kIOHIDVendorIDKey), vid_cf);
    CFDictionarySetValue(match, CFSTR(kIOHIDProductIDKey), pid_cf);

    IOHIDManagerSetDeviceMatching(manager, match);
    IOHIDManagerScheduleWithRunLoop(manager, CFRunLoopGetCurrent(), kCFRunLoopDefaultMode);
    IOReturn open_ret = IOHIDManagerOpen(manager, kIOHIDOptionsTypeNone);
    if (open_ret != kIOReturnSuccess) {
        fprintf(stderr, "Failed to open HID manager: 0x%08X\n", open_ret);
        return 1;
    }

    CFSetRef devices = IOHIDManagerCopyDevices(manager);
    if (!devices || CFSetGetCount(devices) == 0) {
        printf("No Wacom wireless dongle found.\n");
        return 1;
    }

    CFIndex count = CFSetGetCount(devices);
    IOHIDDeviceRef *dev_array = calloc(count, sizeof(IOHIDDeviceRef));
    CFSetGetValues(devices, (const void **)dev_array);

    printf("Found %ld HID service(s):\n\n", (long)count);

    IOHIDDeviceRef target_device = NULL;

    for (CFIndex i = 0; i < count; i++) {
        IOHIDDeviceRef dev = dev_array[i];

        CFNumberRef up_cf = IOHIDDeviceGetProperty(dev, CFSTR(kIOHIDPrimaryUsagePageKey));
        CFNumberRef u_cf  = IOHIDDeviceGetProperty(dev, CFSTR(kIOHIDPrimaryUsageKey));
        CFNumberRef maxin = IOHIDDeviceGetProperty(dev, CFSTR(kIOHIDMaxInputReportSizeKey));

        int usage_page = 0, usage = 0, max_input = 0;
        if (up_cf) CFNumberGetValue(up_cf, kCFNumberIntType, &usage_page);
        if (u_cf)  CFNumberGetValue(u_cf,  kCFNumberIntType, &usage);
        if (maxin) CFNumberGetValue(maxin, kCFNumberIntType, &max_input);

        printf("  Device %ld: UsagePage=0x%04X, Usage=0x%02X, MaxInputReport=%d\n",
               (long)i, usage_page, usage, max_input);

        // Target the wireless monitor interface (UsagePage 0xFF01, Usage 0x80)
        if (usage_page == USAGE_PAGE_WL && usage == USAGE_WL) {
            target_device = dev;
            printf("    ^^^ This is the wireless monitor interface\n");
        }
    }

    if (!target_device) {
        printf("\nWireless monitor interface (0xFF01:0x80) not found.\n");
        free(dev_array);
        return 1;
    }

    // Open the target device
    IOReturn dev_open = IOHIDDeviceOpen(target_device, kIOHIDOptionsTypeNone);
    if (dev_open != kIOReturnSuccess) {
        printf("\nFailed to open wireless monitor device: 0x%08X\n", dev_open);
        free(dev_array);
        return 1;
    }

    printf("\n=== Attempting GetReport (polling) ===\n\n");

    // Try Input type
    try_get_report(target_device, kIOHIDReportTypeInput, "Input", REPORT_ID, REPORT_LEN);

    // Also try Feature type for comparison
    try_get_report(target_device, kIOHIDReportTypeFeature, "Feature", REPORT_ID, REPORT_LEN);

    // Try a few other potentially interesting report IDs
    try_get_report(target_device, kIOHIDReportTypeInput, "Input", 0x02, 32);
    try_get_report(target_device, kIOHIDReportTypeFeature, "Feature", 0x02, 259);
    try_get_report(target_device, kIOHIDReportTypeFeature, "Feature", 0x03, 259);

    printf("\n=== Attempting callback-based input report monitoring (5 seconds) ===\n\n");

    // Register for ALL input reports via callback
    uint8_t report_buf[256];
    IOHIDDeviceRegisterInputReportCallback(target_device, report_buf, sizeof(report_buf),
                                            input_report_callback, NULL);

    // Run the run loop for 5 seconds
    printf("Listening...\n");
    CFRunLoopRunInMode(kCFRunLoopDefaultMode, 5.0, false);

    printf("\n=== Done ===\n");

    IOHIDDeviceClose(target_device, kIOHIDOptionsTypeNone);
    free(dev_array);
    CFRelease(devices);
    IOHIDManagerClose(manager, kIOHIDOptionsTypeNone);
    CFRelease(manager);
    CFRelease(match);
    CFRelease(vid_cf);
    CFRelease(pid_cf);

    return 0;
}
