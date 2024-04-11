#!bin/sh

declare -a files
        
# Add version file
files+=("build/version.txt")

# Add the plugin zip files
files+=("build/plugin/0.5.x/WirelessKitAddon-0.5.x.zip")
files+=("build/plugin/0.6.x/WirelessKitAddon-0.6.x.zip")

# Add all ux zip files
for file in build/ux/*.zip; do
    files+=("$file")
done

gh release create -d -t "Wireless Kit Addon $TAG" "$TAG" "${files[@]}" -F build/hashes.txt