#!/usr/bin/env bash

platform=$1

echo "Parameters:" $@

if [ -z "$platform" ]; then
    echo "Please provide a platform"
    exit 1
fi

if [[ $platform == *"win"* ]]; then
    dotnet publish WirelessKitAddon.UX.Windows -c Release -r $platform -o build/ux/$platform -p:EnableWindowsTargeting=true
else
    dotnet publish WirelessKitAddon.UX.Desktop -c Release -r $platform -o build/ux/$platform
fi