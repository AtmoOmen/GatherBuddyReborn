param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
[xml]$catalog = Get-Content -Raw -LiteralPath "$PSScriptRoot/../build-configuration/DalamudCnReferences.props"
$expected = @{}
foreach ($binary in $catalog.Project.ItemGroup.GbrDalamudCnBinary) {
    $expected[$binary.Include] = [version]$binary.Version
}
$required = @{
    'GatherBuddyReborn.dll' = @('Dalamud', 'FFXIVClientStructs', 'Lumina', 'Lumina.Excel')
    'GatherBuddy.GameData.dll' = @('Dalamud', 'FFXIVClientStructs', 'Lumina', 'Lumina.Excel')
    'GatherBuddy.Levenshtein.dll' = @()
    'GatherBuddy.Models.dll' = @()
    'RebornMaterial.dll' = @('Dalamud', 'FFXIVClientStructs', 'Dalamud.Bindings.ImGui')
}
foreach ($name in $required.Keys) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $name) -PathType Leaf)) {
        throw "Missing expected output: $name"
    }
}
Add-Type -AssemblyName System.Reflection.Metadata
foreach ($dll in Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.dll' -File -Recurse) {
    if ($expected.ContainsKey($dll.BaseName)) {
        throw "Host assembly must not be shipped in plugin output: $($dll.FullName)"
    }
    $stream = $dll.OpenRead()
    $pe = $null
    $observed = @{}
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        if (-not $pe.HasMetadata) {
            if ($required.ContainsKey($dll.Name)) { throw "Missing managed metadata: $($dll.FullName)" }
            continue
        }
        $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($handle in $metadata.AssemblyReferences) {
            $reference = $metadata.GetAssemblyReference($handle)
            $name = $metadata.GetString($reference.Name)
            if (-not $expected.ContainsKey($name)) { continue }
            if ($reference.Version -ne $expected[$name]) {
                throw "$($dll.Name) references $name $($reference.Version), expected CN $($expected[$name])."
            }
            $observed[$name] = $reference.Version
        }
        foreach ($name in $required[$dll.Name]) {
            if (-not $observed.ContainsKey($name)) {
                throw "$($dll.Name) is missing the expected CN $name assembly reference."
            }
        }
        if ($observed.Count) {
            Write-Host ("Verified CN references in {0}: {1}" -f $dll.Name, (($observed.GetEnumerator() | Sort-Object Key | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', '))
        }
    } finally {
        if ($pe) { $pe.Dispose() }
        $stream.Dispose()
    }
}
