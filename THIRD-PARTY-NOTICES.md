# Third-party notices

Vericairn Community is licensed under the Apache License 2.0. Its distributables bundle the third-party components below, each under its own license. Every archive carries the license and notice texts of the components it contains under `licenses/`: the command line's archives carry the files of the first six rows; Workbench's archives, which hold a copy of the command line, carry those and the rest. A test holds this list, the lock files, the shared frameworks the projects reference and the `licenses` directory together, and each release also carries a generated software bill of materials (CycloneDX).

| Component (packages) | Version | License and copyright | License text in the archive | Ships in |
|---|---|---|---|---|
| .NET runtime, libraries and application host (Microsoft) | 10.0 | MIT; Copyright (c) .NET Foundation and Contributors | `licenses/dotnet-LICENSE.txt`, with the runtime's own third-party notices in `licenses/dotnet-THIRD-PARTY-NOTICES.txt` | command line, Workbench |
| ASP.NET Core shared framework (Microsoft.AspNetCore.App), the daemon's HTTP server | 10.0 | MIT; Copyright (c) .NET Foundation and Contributors | `licenses/dotnet-LICENSE.txt` (the same MIT text), with the framework's own third-party notices in `licenses/aspnetcore-THIRD-PARTY-NOTICES.txt` | command line, Workbench |
| System.CommandLine | 2.0.12 | MIT; Copyright (c) Microsoft Corporation | `licenses/MIT-Microsoft.txt` | command line, Workbench |
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core | 10.0.11 | MIT; Copyright (c) Microsoft Corporation | `licenses/MIT-Microsoft.txt` | command line, Workbench |
| SQLitePCLRaw.core, SQLitePCLRaw.provider.e_sqlite3, SQLitePCLRaw.lib.e_sqlite3, SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0; Copyright 2014-2024 SourceGear, LLC | `licenses/Apache-2.0.txt` and `licenses/SQLitePCLRaw-NOTICE.txt` | command line, Workbench |
| SQLite, in the e_sqlite3 native library | per SQLitePCLRaw 2.1.12 | public domain | `licenses/SQLite.txt` | command line, Workbench |
| Avalonia, Avalonia.Desktop, Avalonia.FreeDesktop, Avalonia.FreeDesktop.AtSpi, Avalonia.HarfBuzz, Avalonia.Native, Avalonia.Remote.Protocol, Avalonia.Skia, Avalonia.Themes.Fluent, Avalonia.Win32, Avalonia.X11 | 12.1.4 | MIT; Copyright 2013-2026 © The AvaloniaUI Project | `licenses/MIT-AvaloniaUI.txt` | Workbench |
| Avalonia.Angle.Windows.Natives (ANGLE) | 2.1.27548.20260419 | BSD-3-Clause; Copyright 2018 The ANGLE Project Authors | `licenses/ANGLE-LICENSE.txt` | Workbench on Windows |
| SkiaSharp, SkiaSharp.NativeAssets.Linux, SkiaSharp.NativeAssets.macOS, SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT; Copyright (c) 2015-2016 Xamarin, Inc., Copyright (c) 2017-2018 Microsoft Corporation; the native library's components (Skia and its dependencies) under their own licenses | `licenses/SkiaSharp-HarfBuzzSharp-LICENSE.txt` and `licenses/Skia-HarfBuzz-THIRD-PARTY-NOTICES.txt` | Workbench |
| HarfBuzzSharp, HarfBuzzSharp.NativeAssets.Linux, HarfBuzzSharp.NativeAssets.macOS, HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT; Copyright (c) 2015-2016 Xamarin, Inc., Copyright (c) 2017-2018 Microsoft Corporation; HarfBuzz under its own license | `licenses/SkiaSharp-HarfBuzzSharp-LICENSE.txt` and `licenses/Skia-HarfBuzz-THIRD-PARTY-NOTICES.txt` | Workbench |
| MicroCom.Runtime | 0.11.6 | MIT; Copyright 2021 © Nikita Tsukanov | `licenses/MIT-MicroCom.txt` | Workbench |
| Tmds.DBus.Protocol | 0.94.1 | MIT; Copyright Tom Deseyn | `licenses/MIT-Tmds.DBus.txt` | Workbench on Linux |
| Inter (The Inter Project Authors) | 4.1 | SIL Open Font License 1.1; Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter) | `licenses/OFL-Inter.txt` | Workbench |
| JetBrains Mono (JetBrains) | 2.304 | SIL Open Font License 1.1; Copyright 2020 The JetBrains Mono Project Authors (https://github.com/JetBrains/JetBrainsMono) | `licenses/OFL-JetBrainsMono.txt` | Workbench |

The two fonts are embedded in Workbench's executable. As the SIL Open Font License 1.1 requires, their copyright notices and the license travel with every copy, in each Workbench archive's `licenses/` directory; their source is `src/Vericairn.Workbench/Assets/Fonts`.

Used to build or test Vericairn and not distributed: xunit.v3 and Microsoft.Testing.Platform (Apache-2.0 and MIT), Avalonia.Headless (MIT), Avalonia.BuildServices (MIT, build time only), and the WebAssembly native assets of SkiaSharp and HarfBuzzSharp, which no desktop archive contains.
