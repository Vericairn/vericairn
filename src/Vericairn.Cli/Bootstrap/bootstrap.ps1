# Vericairn estate bootstrap (PowerShell).
# Obtains the Vericairn pinned in estate/manifest.json and runs it against this estate.
# Usage: ./bootstrap.ps1 [vericairn arguments]   (default: estate check)
# In source mode VERICAIRN_SOURCE must be clean and at the pin. For development only,
# VERICAIRN_SOURCE_OVERRIDE=1 allows a different or dirty local source and identifies it explicitly.
# Runs on Windows PowerShell 5.1 and on PowerShell 7 on any platform.
$ErrorActionPreference = 'Stop'
$onWindows = ($env:OS -eq 'Windows_NT')
$root = $PSScriptRoot
$manifestPath = Join-Path $root 'estate/manifest.json'
if (-not (Test-Path $manifestPath)) { Write-Error 'bootstrap: no estate/manifest.json here'; exit 2 }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$mode = $manifest.vericairn.mode
$repository = $manifest.vericairn.repository
$commit = $manifest.vericairn.commit
$work = Join-Path $root '.vericairn/toolchain'
$bin = Join-Path $work 'bin'

switch ($mode) {
  'source' {
    # Native failures are handled explicitly, including the fetch fallback, on every PowerShell version.
    $PSNativeCommandUseErrorActionPreference = $false
    if (-not $commit) { Write-Error 'bootstrap: manifest has no commit'; exit 2 }
    if ($env:VERICAIRN_SOURCE) {
      $src = $env:VERICAIRN_SOURCE
    } else {
      $src = Join-Path $work 'vericairn-src'
      if (-not (Test-Path (Join-Path $src '.git'))) {
        & git clone --quiet "https://github.com/$repository.git" $src
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
      }
      # Windows PowerShell 5.1 can turn redirected native stderr into a terminating error.
      # Let this expected failure reach the exit-code check so the fallback can run.
      $previousPreference = $ErrorActionPreference
      try {
        $ErrorActionPreference = 'Continue'
        & git -C $src fetch --quiet origin $commit 2>$null
        $fetchExit = $LASTEXITCODE
      } finally { $ErrorActionPreference = $previousPreference }
      if ($fetchExit -ne 0) {
        & git -C $src fetch --quiet origin
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
      }
      & git -C $src checkout --quiet --detach $commit
      if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $head = (& git -C $src rev-parse --verify HEAD)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $changes = (& git -C $src status --porcelain --untracked-files=all --ignore-submodules=none)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $dirty = if ($changes) { 'true' } else { 'false' }
    $override = 'false'
    if ($env:VERICAIRN_SOURCE -and $env:VERICAIRN_SOURCE_OVERRIDE -eq '1') {
      $override = 'true'
      Write-Host "bootstrap: development override: commit=$head dirty=$dirty; manifest pins $commit"
    } elseif ($head -ne $commit -or $dirty -eq 'true') {
      [Console]::Error.WriteLine("bootstrap: source is at $head (dirty=$dirty), manifest pins $commit; refusing to run it. For development, set VERICAIRN_SOURCE and VERICAIRN_SOURCE_OVERRIDE=1")
      exit 2
    }
    # The legacy bin/.commit could name a pin that was not built; never reuse it.
    $bin = Join-Path $work "source/$head/bin"
    $stamp = Join-Path $bin '.source'
    $identity = "commit=$head`ndirty=$dirty`noverride=$override`npin=$commit"
    $exe = Join-Path $bin 'vericairn'
    if ($onWindows) { $exe = "$exe.exe" }
    $current = if (Test-Path $stamp) { Get-Content $stamp -Raw } else { '' }
    if ($dirty -eq 'true' -or -not (Test-Path $exe) -or ($current -ne $identity)) {
      # Invalidate before building: a failed build must not leave a reusable stamp or old outputs.
      if (Test-Path $bin) { Remove-Item -Recurse -Force $bin }
      Write-Host "bootstrap: building vericairn from $repository@$head"
      & dotnet build (Join-Path $src 'src/Vericairn.Cli/Vericairn.Cli.csproj') --configuration Release --output $bin --nologo --verbosity quiet
      if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
      Set-Content -Path $stamp -Value $identity -NoNewline
    }
  }
  'release' {
    # The distributable is fetched from the pinned release and verified against the hash in the manifest, never the tag alone.
    $version = $manifest.vericairn.version
    if (-not $version) { Write-Error 'bootstrap: manifest has no version'; exit 2 }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $arch = if ($arch -eq 'arm64') { 'arm64' } else { 'x64' }
    if ($onWindows) { $rid = "win-$arch" }
    elseif ($IsMacOS) { $rid = "osx-$arch" }
    else { $rid = "linux-$arch" }
    $expected = $manifest.vericairn.artifacts.$rid
    if (-not $expected) { Write-Error "bootstrap: the manifest has no artifact hash for $rid"; exit 2 }
    $expected = $expected -replace '^sha256:', ''
    $ext = if ($rid -like 'win-*') { 'zip' } else { 'tar.gz' }
    $name = "vericairn-$version-$rid.$ext"
    $dir = Join-Path $work "release/$version/$rid"
    $archive = Join-Path $dir $name
    $stamp = Join-Path $dir '.sha256'
    $current = if (Test-Path $stamp) { Get-Content $stamp -Raw } else { '' }
    if ($current -ne $expected) {
      if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
      New-Item -ItemType Directory -Path $dir | Out-Null
      $base = if ($env:VERICAIRN_RELEASE_BASE) { $env:VERICAIRN_RELEASE_BASE } else { "https://github.com/$repository/releases/download/v$version" }
      Write-Host "bootstrap: fetching $name from $base"
      if ($base -match '^(https?|file)://') { Invoke-WebRequest -Uri "$base/$name" -OutFile $archive -UseBasicParsing }
      else { Copy-Item (Join-Path $base $name) $archive }
      $actual = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
      if ($actual -ne $expected) {
        Remove-Item -Recurse -Force $dir
        Write-Error "bootstrap: $name has hash $actual, the manifest pins $expected; refusing to run it"; exit 2
      }
      if ($ext -eq 'zip') { Expand-Archive -Path $archive -DestinationPath $dir } else { & tar -xzf $archive -C $dir; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
      Set-Content -Path $stamp -Value $expected -NoNewline
    }
    $bin = Join-Path $dir 'bin'
  }
  default { Write-Error "bootstrap: unknown mode '$mode'"; exit 2 }
}

$exe = Join-Path $bin 'vericairn'
if ($onWindows) { $exe = "$exe.exe" }
if ($args.Count -eq 0) { & $exe --estate $root estate check; exit $LASTEXITCODE }
& $exe --estate $root @args
exit $LASTEXITCODE
