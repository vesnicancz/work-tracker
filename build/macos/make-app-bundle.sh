#!/usr/bin/env bash
#
# Wraps a published Avalonia build into WorkTracker.app and signs it. macOS only — it needs
# sips, iconutil, plutil and codesign.
#
# The published output is a bare apphost, which macOS treats as a faceless Unix binary: no Dock
# name or icon, no bundle identifier for the tray item and the notification backend to attach to,
# and on Apple Silicon no signature, which the kernel refuses outright.
#
# Usage: make-app-bundle.sh <publish-dir> <output-app-path> <version>
#
# Signing is ad-hoc unless CODESIGN_IDENTITY names a real identity. Ad-hoc is what an account-less
# setup can produce: it makes the app launchable on Apple Silicon, but it is not a Gatekeeper pass,
# so a downloaded build still needs one right-click > Open. Going further means a paid Developer ID
# — export it into the runner keychain, set CODESIGN_IDENTITY to it (this script then signs with
# the hardened runtime and entitlements.plist, as notarization requires), and submit the archive
# to notarytool afterwards.

set -euo pipefail

if [[ $# -lt 3 ]]; then
	echo "usage: $(basename "$0") <publish-dir> <output-app-path> <version>" >&2
	exit 2
fi

publish_dir=$1
app_path=$2
raw_version=$3

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd "$script_dir/../.." && pwd)
icon_source=$repo_root/resources/app-ico.png
plist_template=$script_dir/Info.plist
entitlements=$script_dir/entitlements.plist

# Must match CFBundleExecutable in Info.plist, and is the apphost name dotnet publish produces.
executable_name=WorkTracker.Avalonia

# CFBundleShortVersionString takes at most three dot-separated integers, so a tag such as
# v1.14.0-beta.1 has to shed both its prefix and its prerelease part.
version=${raw_version#v}
version=${version%%-*}
if [[ ! $version =~ ^[0-9]+(\.[0-9]+){0,2}$ ]]; then
	echo "warning: '$raw_version' is not a usable bundle version, falling back to 0.0.0" >&2
	version=0.0.0
fi

if [[ ! -f "$publish_dir/$executable_name" ]]; then
	echo "error: no $executable_name in $publish_dir — did dotnet publish run?" >&2
	exit 1
fi

echo "Bundling $publish_dir -> $app_path (version $version)"

rm -rf "$app_path"
mkdir -p "$app_path/Contents/MacOS" "$app_path/Contents/Resources"

# Contents/MacOS may hold nothing but code. codesign seals every file there as a nested code
# object, so a plain data file such as appsettings.json makes --verify --strict fail with "code
# object is not signed at all". Executables and native libraries go there; everything else is a
# resource and belongs in Contents/Resources, which the app resolves via
# WorkTrackerPaths.AppContentDirectory. ditto rather than cp, to carry permissions across unchanged.
while IFS= read -r -d '' item; do
	relative=${item#"$publish_dir"/}
	case $relative in
		"$executable_name" | *.dylib | *.so) destination=$app_path/Contents/MacOS/$relative ;;
		*) destination=$app_path/Contents/Resources/$relative ;;
	esac
	mkdir -p "$(dirname "$destination")"
	ditto "$item" "$destination"
done < <(find "$publish_dir" \( -type f -o -type l \) -print0)

if [[ ! -f "$app_path/Contents/MacOS/$executable_name" ]]; then
	echo "error: $executable_name did not make it into the bundle" >&2
	exit 1
fi
chmod +x "$app_path/Contents/MacOS/$executable_name"

# iconutil insists on this exact set of names inside a .iconset directory. The source icon is
# 760x760, so only the 1024px @2x variant is an upscale; every other size is a clean downscale.
iconset=$(mktemp -d)/WorkTracker.iconset
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
	sips -s format png -z "$size" "$size" "$icon_source" \
		--out "$iconset/icon_${size}x${size}.png" >/dev/null
	sips -s format png -z "$((size * 2))" "$((size * 2))" "$icon_source" \
		--out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil --convert icns "$iconset" --output "$app_path/Contents/Resources/WorkTracker.icns"
rm -rf "$(dirname "$iconset")"

sed "s/@VERSION@/$version/g" "$plist_template" >"$app_path/Contents/Info.plist"
plutil -lint "$app_path/Contents/Info.plist" >/dev/null

identity=${CODESIGN_IDENTITY:-}
identity=${identity:--}
sign_args=(--force --sign "$identity")
if [[ $identity == "-" ]]; then
	echo "Signing ad-hoc"
	# No timestamp server is involved in an ad-hoc signature, and asking for one just fails.
	sign_args+=(--timestamp=none)
else
	echo "Signing with identity: $identity"
	sign_args+=(--timestamp --options runtime --entitlements "$entitlements")
fi

# Nested Mach-O files are signed before the bundle enclosing them. --deep would cover them in one
# call, but Apple deprecated it and it quietly skips whatever it fails to recognise. With
# IncludeNativeLibrariesForSelfExtract the single-file host usually leaves nothing here to find,
# so an empty result is expected rather than a problem.
while IFS= read -r -d '' binary; do
	echo "  signing nested $(basename "$binary")"
	codesign "${sign_args[@]}" "$binary"
done < <(find "$app_path/Contents/MacOS" -type f \( -name '*.dylib' -o -name '*.so' \) -print0)

codesign "${sign_args[@]}" "$app_path"
codesign --verify --strict --verbose=2 "$app_path"

echo "Built $app_path"
