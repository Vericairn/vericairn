# Third-party notices

Vericairn Community is licensed under the Apache License 2.0. It depends on the following third-party
components, each under its own license. This list is maintained with the lock files; a generated
software bill of materials will accompany each release.

| Component | Version | License | Use |
|---|---|---|---|
| .NET runtime and libraries (Microsoft) | 10.0 | MIT | runtime |
| System.CommandLine (dotnet/command-line-api) | 2.0.12 | MIT | command-line parsing in `vericairn` |
| Microsoft.Data.Sqlite (dotnet/efcore) | 10.0.11 | MIT | the live store and the SQLite object store |
| SQLitePCLRaw (ericsink/SQLitePCL.raw) and the bundled SQLite library | per the lock files | Apache-2.0; SQLite is public domain | native SQLite access on every platform |
| xunit.v3 and Microsoft.Testing.Platform | 4.0.2 | Apache-2.0 / MIT | tests only; not distributed |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent and their native dependencies (SkiaSharp, HarfBuzzSharp) | 12.1.4; per the lock files | MIT | Workbench, the desktop |
| Avalonia.Headless | 12.1.4 | MIT | Workbench tests only; not distributed |
| Inter (The Inter Project Authors) | 4.1 | SIL Open Font License 1.1 (`src/Vericairn.Workbench/Assets/Fonts/OFL-Inter.txt`) | Workbench's title, heading and body type |
| JetBrains Mono (JetBrains) | 2.304 | SIL Open Font License 1.1 (`src/Vericairn.Workbench/Assets/Fonts/OFL-JetBrainsMono.txt`) | Workbench's identifiers, hashes and commands |
