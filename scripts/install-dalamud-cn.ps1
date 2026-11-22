param(
    [string]$Destination
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Destination)) {
    if (-not $IsWindows -or [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        throw 'Pass -Destination for the pinned Dalamud CN reference directory.'
    }
    $Destination = Join-Path $env:APPDATA 'XIVLauncherCN/addon/Hooks/dev'
}
$Destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$archive = Join-Path ([IO.Path]::GetTempPath()) ("dalamud-cn-" + [Guid]::NewGuid().ToString('N') + ".7z")
$staging = Join-Path ([IO.Path]::GetTempPath()) ("dalamud-cn-" + [Guid]::NewGuid().ToString('N'))
try {
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/ottercorp/dalamud-distrib/5f709b7ebc53e00dcbf4d201180c6b9faec6abf4/latest.7z' -OutFile $archive
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ne 'e67cee8916d21d2fca5766ceb47311d385bd6c35484f02b00ed8c8ff740f2926') {
        throw 'Dalamud CN archive SHA-256 mismatch.'
    }
    $sevenZip = if ($IsWindows -and (Test-Path (Join-Path $env:ProgramFiles '7-Zip/7z.exe'))) {
        Join-Path $env:ProgramFiles '7-Zip/7z.exe'
    } else {
        (Get-Command 7z -ErrorAction Stop).Source
    }
    & $sevenZip x $archive "-o$staging" -y
    if ($LASTEXITCODE -ne 0) { throw 'Dalamud CN archive extraction failed.' }
    [xml]$catalog = Get-Content -Raw -LiteralPath "$PSScriptRoot/../build-configuration/DalamudCnReferences.props"
    foreach ($binary in $catalog.Project.ItemGroup.GbrDalamudCnBinary) {
        $file = Join-Path $staging "$($binary.Include).dll"
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash -ne $binary.Sha256) {
            throw "Unexpected CN reference hash: $file"
        }
    }
    # Do not mix binaries with another distribution, or silently replace local files.
    if (Test-Path -LiteralPath $Destination) {
        throw "Reference destination already exists: '$Destination'. Choose an unused directory and set GbrDalamudCnPath to it."
    }
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force
    Move-Item -LiteralPath $staging -Destination $Destination
    if ($env:GITHUB_ENV) {
        "GbrDalamudCnPath=$Destination" >> $env:GITHUB_ENV
    }
} finally {
    Remove-Item -LiteralPath $archive,$staging -Recurse -Force -ErrorAction SilentlyContinue
}
