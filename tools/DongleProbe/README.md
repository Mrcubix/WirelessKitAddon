# DongleProbe

Diagnostic tools for debugging the Wacom wireless dongle's HID battery reports on macOS.

## DongleProbe (C# / .NET)

Uses HidSharp to enumerate and probe the dongle's HID interfaces.

**Requirements:** .NET 9 SDK

```
dotnet build macOS/DongleProbe.csproj
dotnet run --project macOS/DongleProbe.csproj
```

## hid_battery_probe (C / native)

Talks directly to IOKit to register for the 32-byte battery input reports that HidSharp cannot see.

**Requirements:** Xcode Command Line Tools (clang)

```
clang -o hid_battery_probe macOS/hid_battery_probe.c -framework IOKit -framework CoreFoundation
./hid_battery_probe
```
