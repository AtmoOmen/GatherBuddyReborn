$ErrorActionPreference = 'Stop'
$name = 'GatherBuddyReborn'
$version = $env:PLUGIN_VERSION
$dll = Get-Item -LiteralPath "build/$name.dll"
if ([Reflection.AssemblyName]::GetAssemblyName($dll.FullName).Version.ToString() -cne $version) {
    throw 'Plugin assembly version differs from the release tag.'
}

$manifest = Get-Content -Raw -LiteralPath "build/$name.json" | ConvertFrom-Json
if ($manifest.InternalName -cne $name -or $manifest.AssemblyVersion -cne $version) {
    throw 'Plugin manifest name or version does not match the release build.'
}

$null = New-Item -ItemType Directory -Path publish -Force
# Remove the SDK package before bundling the complete plugin and raphael-cli.
$sdkPackage = "build/$name"
if (Test-Path -LiteralPath "$sdkPackage/latest.zip") { Remove-Item -Recurse -LiteralPath $sdkPackage }
Get-ChildItem -LiteralPath build -File -Filter '*.pdb' | Remove-Item

foreach ($file in "$name.dll", "$name.json", 'raphael-cli.exe', 'LICENSE.raphael-rs.txt',
        'LICENSE.GatherBuddyReborn.txt', 'LICENSE.RebornMaterial-LGPL-3.0.txt',
        'LICENSE.RebornMaterial-GPL-3.0.txt', 'RebornMaterial-NOTICE.txt') {
    if ((Get-Item -LiteralPath "build/$file").Length -eq 0) { throw "Empty release component: $file" }
}
$zip = 'publish/latest.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
[IO.Compression.ZipFile]::CreateFromDirectory((Resolve-Path build).Path, $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($zip))
Get-FileHash -Algorithm SHA256 -LiteralPath $zip
