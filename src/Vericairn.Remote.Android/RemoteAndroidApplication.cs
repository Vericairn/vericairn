// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
// The presence key's prompt: a strong biometric or the device credential before each signature.
[assembly: UsesPermission(Android.Manifest.Permission.UseBiometric)]

namespace Vericairn.Remote.Android;

/// <summary>
/// Starts Remote with this device's storage and Android Keystore: a request store, and from Android 11 a presence store whose keys
/// need the owner's presence at every signature. Backup is off: a replacement phone pairs afresh rather than inheriting connections
/// from a backup, and Keystore keys never leave the device anyway.
/// </summary>
[Application(AllowBackup = false, Label = "Remote")]
public sealed class RemoteAndroidApplication : AvaloniaAndroidApplication<RemoteApp>
{
    /// <summary>Called by the Android runtime.</summary>
    public RemoteAndroidApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    /// <inheritdoc />
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        RemoteApp.Connections = () => new LocalNetworkConnections(Home(), new AndroidKeystoreDeviceKeyStore(), PresenceKeys());
        // The system font size setting, as a scale of the default.
        RemoteApp.TextScale = global::Android.App.Application.Context.Resources?.Configuration?.FontScale ?? 1;
        return base.CustomizeAppBuilder(builder);
    }

    /// <summary>The presence key's store: Android Keystore with per-use authentication, from Android 11. Earlier versions have none and sign no decision.</summary>
    private static AndroidKeystoreDeviceKeyStore? PresenceKeys() => OperatingSystem.IsAndroidVersionAtLeast(30) ? new AndroidKeystoreDeviceKeyStore(presence: true) : null;

    private static string Home()
    {
        string home = Path.Combine(global::Android.App.Application.Context.FilesDir!.AbsolutePath, "vericairn");
        Directory.CreateDirectory(home);
        return home;
    }
}

/// <summary>Remote's one activity.</summary>
[Activity(Label = "Remote", MainLauncher = true, Theme = "@style/Theme.AppCompat.DayNight.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class RemoteActivity : AvaloniaMainActivity
{
}
