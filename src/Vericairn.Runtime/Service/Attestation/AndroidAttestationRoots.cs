// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography.X509Certificates;

namespace Vericairn.Runtime.Service;

internal static class AndroidAttestationRoots
{
    // Public RSA and EC trust anchors published at https://android.googleapis.com/attestation/root.
    internal static X509Certificate2Collection Load()
    {
        using Stream pem = typeof(AndroidAttestationRoots).Assembly.GetManifestResourceStream("Vericairn.AndroidAttestationRoots.pem")
            ?? throw new InvalidOperationException("The runtime has no embedded Android attestation roots.");
        using var reader = new StreamReader(pem);
        var roots = new X509Certificate2Collection();
        roots.ImportFromPem(reader.ReadToEnd());
        return roots;
    }
}
