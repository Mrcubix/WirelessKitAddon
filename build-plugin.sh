#!/usr/bin/env bash

# ------------------------- Variables ------------------------- #

versions=("0.5.x" "0.6.x")

# ------------------------- Arguments ------------------------- #

donotzip=false

if [ $# -eq 1 ] && [ "$1" == "--no-zip" ];
then
    donotzip=true
fi

# ------------------------- Functions ------------------------- #

create_build_structure () {
    # Check if build directory exists
    if [ -d "build" ];
    then
        # Clear the build directory if it exists
        echo "Removing all files in build"
        rm -rf build/*
    else
        # Attempt to create the build directory if it does not exist
        if ! mkdir build 2> /dev/null;
        then
            if [ ! -d "build" ];
            then
                echo "Failed to create the 'build' directory"
                exit 1
            fi
        fi
    fi
}

create_plugin_structure () {
    (
        cd build

        #create plugin folder for the specified version
        if ! mkdir -p plugin/$version 2> /dev/null;
        then
            if [ ! -d "plugin/$version" ];
            then
                echo "Failed to create the 'build/plugin/$version' directory"
                exit 1
            fi
        fi
    )
}

# ------------------------- Main ------------------------- #

create_build_structure

for version in "${versions[@]}"
do
    create_plugin_structure

    echo "Building WirelessKitAddon-$version"
    
    # Exit on failure
    dotnet publish WirelessKitAddon-$version -c Debug -o ./temp/plugin/$version || exit 1

    # Move WirelessKitAddon-$version.dll to ./build/plugin/$version then cd to it and zip it
    mv ./temp/plugin/$version/WirelessKitAddon.dll ./build/plugin/$version
    mv ./temp/plugin/$version/WirelessKitAddon.Lib.dll ./build/plugin/$version
    mv ./temp/plugin/$version/CommunityToolkit.Mvvm.dll ./build/plugin/$version

    if [ "$donotzip" = true ];
    then
        continue
    fi

    (
        cd ./build/plugin/$version
        #zip -r WirelessKitAddon-$version.zip ./*
        jar -cfM WirelessKitAddon-$version.zip ./*
    )

done

rm -rf ./temp

echo ""
echo "Plugin build complete"
echo ""