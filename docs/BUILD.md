# Building Sufni.App from the terminal

All commands are run from the repository root (`Sufni.App/`).

## Prerequisites

- .NET SDK 10.0 (`dotnet --version` should print `10.0.*`).
- Platform workloads: `dotnet workload install macos ios android` on
  macOS, `dotnet workload install android` on Linux/Windows. Not needed
  for Linux/Windows desktop builds.
- iOS device/simulator deployment also needs Xcode installed at
  `/Applications/Dev/Xcode.app` and a Microsoft.iOS SDK pack under
  `/usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.2/…`
  (installed with the `ios` workload). The exact pack version in the
  commands below may need updating as the SDK changes.
- Android builds need the Android SDK. Either set `ANDROID_HOME` to the
  SDK root or pass `-p:AndroidSdkDirectory=<path>` on the command line.
  Without it the build fails with `error XA5300: The Android SDK
directory could not be found`. `adb` (from `platform-tools`) must be
  on `PATH` for device management.

## Solution layout

Committed solutions map directly to developer scenarios:

| Solution            | Intended use                                                         |
| ------------------- | -------------------------------------------------------------------- |
| `Sufni.App.sln`     | Full matrix / repo-wide solution                                     |
| `Sufni.Desktop.sln` | Desktop work: shared app, desktop layer, desktop heads, shared tests |
| `Sufni.Android.sln` | Android work: shared app, Android head, neutral libraries            |
| `Sufni.iOS.sln`     | iOS work: shared app, iOS head, neutral libraries                    |

Each head project under `Sufni.App/` targets one platform and references
the shared `Sufni.App/Sufni.App/Sufni.App.csproj` directly or through the
desktop layer where appropriate:

| Head project        | TargetFramework   | Output bundle / binary                                              |
| ------------------- | ----------------- | ------------------------------------------------------------------- |
| `Sufni.App.macOS`   | `net10.0-macos`   | `bin/Debug/net10.0-macos/<rid>/Sufni.App.macOS.app`                 |
| `Sufni.App.Linux`   | `net10.0`         | `bin/Debug/net10.0/Sufni.App.Linux.dll`                             |
| `Sufni.App.Windows` | `net10.0-windows` | `bin/Debug/net10.0-windows/Sufni.App.Windows.exe`                   |
| `Sufni.App.iOS`     | `net10.0-ios`     | `bin/Debug/net10.0-ios/<rid>/Sufni.App.iOS.app`                     |
| `Sufni.App.Android` | `net10.0-android` | `bin/Debug/net10.0-android/<rid>/com.sghctoma.Sufni.App-Signed.apk` |

## macOS

```sh
# Build
dotnet build Sufni.App/Sufni.App.macOS/Sufni.App.macOS.csproj -c Debug

# Run (Apple Silicon)
open Sufni.App/Sufni.App.macOS/bin/Debug/net10.0-macos/osx-arm64/Sufni.App.macOS.app
```

On Intel Macs, substitute `osx-x64` for `osx-arm64`.
One-shot build+run: `dotnet run --project Sufni.App/Sufni.App.macOS/Sufni.App.macOS.csproj -c Debug`.

## Linux

```sh
# Build
dotnet build Sufni.App/Sufni.App.Linux/Sufni.App.Linux.csproj -c Debug

# Run
dotnet run --project Sufni.App/Sufni.App.Linux/Sufni.App.Linux.csproj -c Debug
```

### Cross-publish from macOS

To produce a self-contained Linux x64 executable from a macOS host:

```sh
dotnet publish Sufni.App/Sufni.App.Linux/Sufni.App.Linux.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:UseMonoRuntime=false
```

The output bundle lands at
`Sufni.App/Sufni.App.Linux/bin/Release/net10.0/linux-x64/publish/`. Copy
the whole directory to the Linux target and run `./Sufni.App.Linux`.

`-p:UseMonoRuntime=false` is required because `Sufni.App.csproj` and the
support libraries multi-target `net10.0-android`. Without it, NuGet
propagates the `linux-x64` RID into the Android TFM during transitive
restore and demands `Microsoft.NETCore.App.Runtime.Mono.linux-x64`, which
doesn't exist (Mono runtime packs only ship for mobile / wasm /
maccatalyst RIDs). Setting the property disables the Mono pack lookup
for the unused Android restore branch; the actual Linux build uses
CoreCLR and is unaffected.

For arm64, substitute `linux-arm64` for `linux-x64`.

## Windows

```powershell
# Build
dotnet build Sufni.App\Sufni.App.Windows\Sufni.App.Windows.csproj -c Debug

# Run
dotnet run --project Sufni.App\Sufni.App.Windows\Sufni.App.Windows.csproj -c Debug
```

### Cross-publish from macOS

To produce a self-contained Windows x64 executable from a macOS host:

```sh
dotnet publish Sufni.App/Sufni.App.Windows/Sufni.App.Windows.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:UseMonoRuntime=false
```

The output bundle lands at
`Sufni.App/Sufni.App.Windows/bin/Release/net10.0-windows/win-x64/publish/`.
Copy the whole directory to the Windows target and run
`Sufni.App.Windows.exe`.

`-p:UseMonoRuntime=false` is required for the same reason as the Linux
cross-publish above: NuGet propagates the `win-x64` RID into the
`net10.0-android` TFM of the multi-targeted dependencies during restore
and tries to fetch `Microsoft.NETCore.App.Runtime.Mono.win-x64`, which
doesn't exist either.

For arm64, substitute `win-arm64` for `win-x64`.

## iOS

The iOS head (`Sufni.App/Sufni.App.iOS/Sufni.App.iOS.csproj`) currently
pins a single `RuntimeIdentifier` in the csproj (either `ios-arm64` or
`iossimulator-arm64`). You can either edit that line to match your target,
or override it from the command line with `-r <rid>
-p:RuntimeIdentifier=<rid>` — the CLI override wins over the csproj value.

Bundle id: `com.moridinbg.Sufni.App`.

For personal iOS device signing with a different Apple team or bundle id,
run the setup helper before building:

```sh
scripts/setup-ios-personal-signing.sh \
  --bundle-id com.example.Sufni.App \
  --team-id ABCDE12345
```

### Simulator

```sh
# Build for simulator (Apple Silicon host)
dotnet build Sufni.App/Sufni.App.iOS/Sufni.App.iOS.csproj \
  -c Debug -f net10.0-ios -r iossimulator-arm64 -p:RuntimeIdentifier=iossimulator-arm64

# List available simulator UDIDs
xcrun simctl list devices available

# Launch (replace <UDID> with one from the list above)
/usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.2/26.2.10217/tools/bin/mlaunch \
  --launchsim Sufni.App/Sufni.App.iOS/bin/Debug/net10.0-ios/iossimulator-arm64/Sufni.App.iOS.app \
  --device=:v2:udid=<UDID> \
  -sdk 26.2 \
  --sdkroot /Applications/Dev/Xcode.app/Contents/Developer
```

For Intel hosts, use `iossimulator-x64` in both the `-r` flag and the
output path.

### Device

```sh
# Build for a physical device
dotnet build Sufni.App/Sufni.App.iOS/Sufni.App.iOS.csproj \
  -c Debug -f net10.0-ios -r ios-arm64 -p:RuntimeIdentifier=ios-arm64

# List connected devices (to confirm the device name)
/usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.2/26.2.10217/tools/bin/mlaunch \
  --listdev

# Install the freshly built bundle onto the device
/usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.2/26.2.10217/tools/bin/mlaunch \
  --devname "iPhone" \
  --installdev Sufni.App/Sufni.App.iOS/bin/Debug/net10.0-ios/ios-arm64/Sufni.App.iOS.app \
  --wait-for-unlock \
  -sdk 13.0 \
  --sdkroot /Applications/Dev/Xcode.app/Contents/Developer

# Kill any existing instance and launch
/usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.2/26.2.10217/tools/bin/mlaunch \
  --devname "iPhone" \
  --killdev com.moridinbg.Sufni.App \
  --launchdev Sufni.App/Sufni.App.iOS/bin/Debug/net10.0-ios/ios-arm64/Sufni.App.iOS.app \
  --wait-for-unlock \
  --argument=-connection-mode --argument=usb \
  -sdk 13.0 \
  --sdkroot /Applications/Dev/Xcode.app/Contents/Developer
```

Replace `"iPhone"` with the name reported by `--listdev` (keep the
exact quoting, including any curly apostrophe `’`). `--installdev` and
`--launchdev` are two separate steps: `--launchdev` alone just starts
whatever version is already on the device, so always run `--installdev`
first after a fresh build.

## Android

Application id: `com.sghctoma.Sufni.App`. The Android head project
produces a signed debug APK at
`bin/Debug/net10.0-android/<rid>/com.sghctoma.Sufni.App-Signed.apk`.
Typical RIDs are `android-arm64` for physical devices and most recent
emulators, `android-x64` for x86_64 emulators, and `android-arm` for
older 32-bit devices.

### Device or emulator

```sh
# Build the APK
dotnet build Sufni.App/Sufni.App.Android/Sufni.App.Android.csproj \
  -c Debug -f net10.0-android -r android-arm64 -p:RuntimeIdentifier=android-arm64

# List connected devices and running emulators
adb devices

# Install + launch in one step via the .NET Android MSBuild target
dotnet build Sufni.App/Sufni.App.Android/Sufni.App.Android.csproj \
  -c Debug -f net10.0-android -r android-arm64 -p:RuntimeIdentifier=android-arm64 \
  -t:Run
```

The `-t:Run` target rebuilds if needed, installs the APK onto the
currently selected `adb` device, and launches the app. If multiple
devices are connected, select one by exporting `ANDROID_SERIAL=<serial>`
(get the serial from `adb devices`) before running the command, or pass
`-p:AdbTarget="-s <serial>"`.

Plain `adb` also works if you prefer to drive install/launch yourself:

```sh
# Install the freshly built APK
adb install -r Sufni.App/Sufni.App.Android/bin/Debug/net10.0-android/android-arm64/com.sghctoma.Sufni.App-Signed.apk

# Launch
adb shell monkey -p com.sghctoma.Sufni.App -c android.intent.category.LAUNCHER 1

# Stop a running instance
adb shell am force-stop com.sghctoma.Sufni.App
```

### Emulator startup

If no emulator is running, start one via the Android SDK's `emulator`
tool:

```sh
# List configured AVDs
"$ANDROID_HOME/emulator/emulator" -list-avds

# Start one (replace <avd> with a name from the list)
"$ANDROID_HOME/emulator/emulator" -avd <avd> &

# Wait until it's ready before installing
adb wait-for-device
```
