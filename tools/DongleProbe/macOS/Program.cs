using HidSharp;

const int WACOM_VID = 0x056A;
const int WIRELESS_KIT_PID = 0x0084;

Console.WriteLine("=== Wacom Wireless Dongle Probe ===\n");

var devices = DeviceList.Local.GetHidDevices(WACOM_VID, WIRELESS_KIT_PID).ToList();

if (devices.Count == 0)
{
    Console.WriteLine("No Wacom wireless dongle found.");
    return;
}

Console.WriteLine($"Found {devices.Count} endpoint(s):\n");
foreach (var dev in devices)
{
    Console.WriteLine($"  InputLen={dev.GetMaxInputReportLength(),-4} " +
                      $"OutputLen={dev.GetMaxOutputReportLength(),-4} " +
                      $"FeatureLen={dev.GetMaxFeatureReportLength()}");
}
Console.WriteLine();

// === Probe the control endpoint (FeatureLen=259) ===
var controlDev = devices.FirstOrDefault(d => d.GetMaxFeatureReportLength() == 259);
if (controlDev == null)
{
    Console.WriteLine("Control endpoint (FeatureLen=259) not found.");
    return;
}

Console.WriteLine("=== Control Endpoint (FeatureLen=259) ===\n");
HidStream? controlStream = null;
try
{
    if (!controlDev.TryOpen(out controlStream))
    {
        Console.WriteLine("Could not open control endpoint.");
        return;
    }

    // Step 1: Read feature report 0x02 BEFORE init
    Console.WriteLine("Before init command:");
    TryReadFeature(controlStream, 0x02, 259);
    TryReadFeature(controlStream, 0x03, 259);

    // Step 2: Send the init feature report (0x02, 0x02) like the handler does
    Console.WriteLine("\nSending init feature report {0x02, 0x02}...");
    try
    {
        var initReport = new byte[259];
        initReport[0] = 0x02;
        initReport[1] = 0x02;
        controlStream.SetFeature(initReport);
        Console.WriteLine("  Init sent successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Init FAILED: {ex.Message}");
    }

    // Step 3: Wait a moment, then read again
    Thread.Sleep(500);
    Console.WriteLine("\nAfter init command:");
    TryReadFeature(controlStream, 0x02, 259);
    TryReadFeature(controlStream, 0x03, 259);

    // Step 4: Try a broader range of report IDs
    Console.WriteLine("\nScanning all report IDs (0x00-0x0F, 0x80, 0xC0):");
    foreach (byte id in new byte[] { 0x00, 0x01, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x80, 0xC0 })
    {
        TryReadFeature(controlStream, id, 259);
    }
}
finally
{
    controlStream?.Dispose();
}

// === Probe the 10-byte input endpoint ===
Console.WriteLine("\n=== 10-byte Input Endpoint ===\n");
var inputDev = devices.FirstOrDefault(d => d.GetMaxInputReportLength() == 10);
if (inputDev != null)
{
    HidStream? inputStream = null;
    try
    {
        if (inputDev.TryOpen(out inputStream))
        {
            Console.WriteLine("Listening for input reports (5 seconds)...");
            inputStream.ReadTimeout = 5000;
            var startTime = DateTime.Now;
            int count = 0;
            while ((DateTime.Now - startTime).TotalSeconds < 5 && count < 10)
            {
                try
                {
                    var buf = new byte[10];
                    var bytesRead = inputStream.Read(buf, 0, buf.Length);
                    Console.Write($"  Report #{count}: ");
                    for (int i = 0; i < bytesRead; i++)
                        Console.Write($"{buf[i]:X2} ");
                    Console.WriteLine();
                    count++;
                }
                catch (TimeoutException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Read failed: {ex.Message}");
                    break;
                }
            }
            if (count == 0)
                Console.WriteLine("  No input reports received.");
        }
        else
            Console.WriteLine("Could not open 10-byte endpoint.");
    }
    finally
    {
        inputStream?.Dispose();
    }
}

// === Probe the 64-byte input endpoint ===
Console.WriteLine("\n=== 64-byte Input Endpoint ===\n");
var auxDev = devices.FirstOrDefault(d => d.GetMaxInputReportLength() == 64);
if (auxDev != null)
{
    HidStream? auxStream = null;
    try
    {
        if (auxDev.TryOpen(out auxStream))
        {
            Console.WriteLine("Listening for input reports (3 seconds)...");
            auxStream.ReadTimeout = 3000;
            int count = 0;
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalSeconds < 3 && count < 5)
            {
                try
                {
                    var buf = new byte[64];
                    var bytesRead = auxStream.Read(buf, 0, buf.Length);
                    Console.Write($"  Report #{count}: ");
                    for (int i = 0; i < Math.Min(bytesRead, 32); i++)
                        Console.Write($"{buf[i]:X2} ");
                    if (bytesRead > 32)
                        Console.Write("...");
                    Console.WriteLine();
                    count++;
                }
                catch (TimeoutException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Read failed: {ex.Message}");
                    break;
                }
            }
            if (count == 0)
                Console.WriteLine("  No input reports received.");
        }
        else
            Console.WriteLine("Could not open 64-byte endpoint.");
    }
    finally
    {
        auxStream?.Dispose();
    }
}

Console.WriteLine("\n=== Probe complete ===");

static void TryReadFeature(HidStream stream, byte reportId, int featureLen)
{
    try
    {
        var buf = new byte[featureLen];
        buf[0] = reportId;
        stream.GetFeature(buf);

        // Check if all zeros
        bool allZero = true;
        for (int i = 0; i < buf.Length; i++)
            if (buf[i] != 0) { allZero = false; break; }

        if (allZero)
        {
            Console.WriteLine($"  0x{reportId:X2}: all zeros");
            return;
        }

        Console.Write($"  0x{reportId:X2}: ");
        int printLen = Math.Min(buf.Length, 40);
        for (int i = 0; i < printLen; i++)
            Console.Write($"{buf[i]:X2} ");
        if (buf.Length > printLen)
            Console.Write("...");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  0x{reportId:X2}: FAILED - {ex.Message}");
    }
}
