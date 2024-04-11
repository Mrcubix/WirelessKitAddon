#!/usr/bin/env bash

# ------------------------- Variables ------------------------- #

donotzip=false

if [ $# -eq 1 ] && [ "$1" == "--no-zip" ];
then
    donotzip=true
fi

# ------------------------- Functions ------------------------- #

zip_dir_contents () {
    echo "Zipping $f"
    (
        cd $1
        zip -r $2 ./*
    )
}

# ------------------------- Main ------------------------- #

if [ -d "./build/ux" ]; then
    rm -rf ./build/ux/*
fi

echo ""
echo "Building WirelessKitAddon.UX.Desktop"
echo ""

# build the desktop on linux & windows
platforms=("linux-x64" "linux-arm64" "linux-arm" "win-x64" "win-x86" "win-arm64" "osx-x64" "osx-arm64")

for platform in "${platforms[@]}"
do
    # Windows needs its own project because of a bad dependency
    if [[ $platform == *"win"* ]]; then
        dotnet publish WirelessKitAddon.UX.Windows -c Release -r $platform -o build/ux/$platform
    else
        dotnet publish WirelessKitAddon.UX.Desktop -c Release -r $platform -o build/ux/$platform
    fi
done

find ./build/ux -name "*.pdb" -type f -delete

if [ $donotzip == true ];
then
    echo "Skipping zipping of UX"
    exit 0
fi

find ./build/ux -name "*.pdb" -type f -delete

# zip all the files
(
    cd ./build/ux

    for f in *; do
        if [ -d "$f" ]; then
            zip_dir_contents $f "../WirelessKitBatteryStatus.UX-$f.zip"
        fi
    done
)



echo ""
echo "UX built successfully"
echo ""