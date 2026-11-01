# Remote

Remote is Vericairn's phone companion for iOS and Android: a client of an estate's runtime on your local network, never a host
of one and never a separate estate. It uses the same presentation, state language and SDK as Workbench.

This guide describes Remote's first build (REMOTE-01, 0.9.0). It is a **developer build**: it is not in an app store, and no
real-device qualification is recorded for it.

## What this build does

- **First use** explains the companion role and pairs the phone with an estate you already run.
- **Estates:** the phone keeps a connection to each estate it paired with; you choose which one the home shows, and pair with
  another from the home.
- **Home**, for the selected estate, with its connection state (connecting, current, cached, partial, denied or unavailable) and
  how fresh the content is:
  - **Attention:** what the runtime's status says needs a person, most urgent first.
  - **Work:** work orders that are not accepted or retired, and assignments that are not accepted.
  - **Products:** the estate's products.
  - **Nodes:** the runtime this phone reaches: its identity, address, who the phone acts as, the phone's device and the last
    time a check was current. A healthy connection here says nothing about workers running on other machines.
  - **Handoffs:** not in Remote yet; prepare and open them in Workbench.

Remote decides, accepts and changes nothing in this build. Opening a subject, acknowledging a notice, work detail and
communicating with a contributor are not in it yet.

Content shown after the runtime can no longer be reached is labelled cached, with the time it was current. A restored screen
restores no permission: opening the app checks the selected estate again.

## Pairing a phone

On the machine running the estate's runtime:

1. Give the runtime a local-network address that the phone can reach, then start it:

   ```sh
   vericairn runtime configure --local-network 192.168.1.20:7443
   vericairn runtime start
   ```

2. Issue a pairing code for the person who will use the phone. The operator or a principal holding the `administer` scope
   runs this:

   ```sh
   vericairn devices pair --principal <principal> --by <administrator>
   ```

   The code has 16 characters, is valid for ten minutes, and admits one device in at most five attempts.

On the phone, enter the runtime's address, the code and a name for the phone, then choose **Pair**. iOS asks once whether
Remote may use the local network. The runtime proves its identity and its knowledge of the code; the phone creates its own
key and presents it; the runtime records the device and admits it. The connection pins the runtime's identity.

## Keys and custody

The phone's device key never leaves the phone, and its device record says how the key is held:

| Where | Custody | What it means |
| --- | --- | --- |
| iPhone or iPad | `ios-secure-enclave` | Generated in the Secure Enclave; usable only on that device while it is unlocked; never exported or backed up. |
| Android | `android-keystore` | Generated in Android Keystore; never exported; hardware-backed where the device provides it. |
| iOS simulator | `file-owner-only` | The simulator has no Secure Enclave, so the key is an owner-only file in the app's storage. |

The key signs every request to the runtime without asking for Face ID, Touch ID or a passcode, so the phone's device does
not claim presence. Remote's connections are excluded from backups: a replacement phone pairs again instead of inheriting
access. To retire a lost phone, revoke its device with `vericairn devices revoke <device>`. That erases no estate history.
Rotating a phone's key is not built yet; pair the phone again and revoke the old device.

## Building from source

The phone heads are in `src/Vericairn.Mobile.slnx`, not in `Vericairn.slnx`, because each needs its platform's workload:

- iOS: the .NET `ios` workload (workload set `10.0.303.1`) and Xcode 26.5 or later on macOS.
  `dotnet build src/Vericairn.Remote.iOS/Vericairn.Remote.iOS.csproj` builds for the Apple-silicon simulator.
- Android: the .NET `android` workload (workload set `10.0.303.1`), an Android SDK and JDK 17.
  `dotnet build src/Vericairn.Remote.Android/Vericairn.Remote.Android.csproj -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk>`.

Continuous integration builds both heads on every change: iOS on macOS, Android on Linux. Installing on a real iPhone needs
your own Apple signing; Remote is not distributed through the App Store or Google Play.

## Limits

- No real-device qualification is recorded; simulator and CI builds prove neither.
- Remote reaches a runtime only on the same local network; the relay that would reach it from elsewhere is a design.
- The shared libraries are not trim-safe yet, so the phone apps trim only the platform's own assemblies.
