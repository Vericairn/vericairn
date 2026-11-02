// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Avalonia;
using Avalonia.iOS;
using Foundation;
using ObjCRuntime;
using UIKit;
using Vericairn.Records.Signing;

namespace Vericairn.Remote.iOS;

/// <summary>Starts Remote with this device's storage and key store.</summary>
[Register("AppDelegate")]
public partial class RemoteIosApplication : AvaloniaAppDelegate<RemoteApp>
{
    /// <inheritdoc />
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        RemoteApp.Connections = () => new LocalNetworkConnections(Home(), Keys(), PresenceKeys());
        // Dynamic Type: the body text style's size against its default of 17 points.
        RemoteApp.TextScale = (UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body)?.PointSize ?? 17) / 17;
        return base.CustomizeAppBuilder(builder);
    }

    /// <summary>
    /// The Secure Enclave on a device. The simulator has none, so a simulator build keeps its key in an owner-only file in the app's
    /// storage and records exactly that custody.
    /// </summary>
    private static IDeviceKeyStore Keys() => Runtime.Arch == Arch.SIMULATOR ? new FileKeyStore(Home()) : new SecureEnclaveDeviceKeyStore();

    /// <summary>
    /// The presence key's store: the Secure Enclave with user presence on a device. The simulator's owner-only file cannot require
    /// presence, so a simulator build has no presence key, claims no presence and signs no decision.
    /// </summary>
    private static SecureEnclaveDeviceKeyStore? PresenceKeys() => Runtime.Arch == Arch.SIMULATOR ? null : new SecureEnclaveDeviceKeyStore(presence: true);

    // Connections and any simulator key live in the app's own Application Support, excluded from backups: a replacement phone pairs
    // afresh rather than inheriting access from a backup.
    private static string Home()
    {
        NSUrl support = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0];
        string home = Path.Combine(support.Path!, "Vericairn");
        Directory.CreateDirectory(home);
        NSUrl.FromFilename(home).SetResource(NSUrl.IsExcludedFromBackupKey, NSNumber.FromBoolean(true), out _);
        return home;
    }
}

/// <summary>The process entry point.</summary>
public static class Program
{
    /// <summary>Runs the application.</summary>
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(RemoteIosApplication));
}
