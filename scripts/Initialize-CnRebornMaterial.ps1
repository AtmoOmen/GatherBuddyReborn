# Stage the unmodified LGPL-3.0-or-later RebornMaterial sources for the CN build.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$url = 'https://github.com/LTS-FFXIV/RebornMaterial.git'
$tag = '7.5.8.4'
$commit = '0c74204ecfd6785c27ac53213950c3ad08337e63'
$vendor = Join-Path (Split-Path -Parent $PSScriptRoot) 'build-configuration/vendor'
$destination = Join-Path $vendor 'RebornMaterial'

function Invoke-Git {
    & git @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($args -join ' ') failed (exit $LASTEXITCODE)."
    }
}

function Assert-RebornMaterialSource([string] $Path) {
    if (-not (Test-Path -LiteralPath (Join-Path $Path '.git') -PathType Container)) {
        throw "Expected a dedicated RebornMaterial checkout at '$Path'."
    }

    $head = Invoke-Git -C $Path rev-parse --verify HEAD
    if ($head -cne $commit) {
        throw "RebornMaterial must be at $commit (tag $tag); found $head in '$Path'."
    }

    $changes = Invoke-Git -C $Path status --porcelain=v1 --untracked-files=all
    if ($changes) {
        throw "RebornMaterial sources have local changes in '$Path'. Move that checkout aside before retrying."
    }
}

if (Test-Path -LiteralPath $destination) {
    Assert-RebornMaterialSource $destination
} else {
    New-Item -ItemType Directory -Force -Path $vendor | Out-Null
    $staging = Join-Path $vendor ('.RebornMaterial-' + [guid]::NewGuid().ToString('N'))
    try {
        Invoke-Git clone --quiet --depth 1 --single-branch --branch $tag -- $url $staging
        Assert-RebornMaterialSource $staging
        [System.IO.Directory]::Move($staging, $destination)
    } finally {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
}

Write-Output "RebornMaterial $tag ($commit) is ready at '$destination'."
