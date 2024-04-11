versions=("0.5.x" "0.6.x")

if ! bash ./build-plugin.sh; then
    echo "Build failed"
    exit 1
fi

if ! bash ./build-ux.sh; then
    echo "Build failed"
    exit 1
fi

# Re-create hashes.txt
> "./build/hashes.txt"

# Append all hashes to hashes.txt
(
    cd ./build

    output="../hashes.txt"

    (
        cd ./plugin

        # Compute all Plugin Hashes
        for version in "${versions[@]}"
        do
            echo "Computing WirelessKitAddon-$version.zip"
            sha256sum $version/WirelessKitAddon-$version.zip >> $output
        done
    )

    echo "" >> hashes.txt

    (
        cd ./ux

        # Compute all UX Hashes

        for os in win linux osx; do
            for arch in x64 x86 arm64; do

                name="WirelessKitBatteryStatus.UX-$os-$arch.zip"

                echo "Computing $name"

                if [ -f "$name" ]; then
                    sha256sum $name >> $output
                fi
            done
        done
    )
)