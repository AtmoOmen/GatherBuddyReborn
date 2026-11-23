# Stage the pinned RebornMaterial sources with the mainland UI translation.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$url = 'https://github.com/LTS-FFXIV/RebornMaterial.git'
$tag = '7.5.8.4'
$commit = '0c74204ecfd6785c27ac53213950c3ad08337e63'
$buildConfiguration = Join-Path (Split-Path -Parent $PSScriptRoot) 'build-configuration'
$localizationPatch = Join-Path $buildConfiguration 'RebornMaterial.zh-CN.patch'
$vendor = Join-Path $buildConfiguration 'vendor'
$destination = Join-Path $vendor 'RebornMaterial'

function Invoke-Git {
    & git @args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($args -join ' ') failed (exit $LASTEXITCODE)."
    }
}

function Get-LocalizedTree([string] $Path) {
    $previousIndex = $env:GIT_INDEX_FILE
    $temporaryIndex = Join-Path ([System.IO.Path]::GetTempPath()) ('gbr-m3-index-' + [guid]::NewGuid().ToString('N'))
    try {
        $env:GIT_INDEX_FILE = $temporaryIndex
        Invoke-Git -C $Path read-tree $commit
        Invoke-Git -C $Path apply --cached -- $localizationPatch
        Invoke-Git -C $Path write-tree
    } finally {
        $env:GIT_INDEX_FILE = $previousIndex
        Remove-Item -LiteralPath $temporaryIndex -Force -ErrorAction SilentlyContinue
    }
}

function Initialize-RebornMaterialSource([string] $Path) {
    if (-not (Test-Path -LiteralPath (Join-Path $Path '.git') -PathType Container)) {
        throw "Expected a dedicated RebornMaterial checkout at '$Path'."
    }

    $head = Invoke-Git -C $Path rev-parse --verify HEAD
    if ($head -cne $commit) {
        throw "RebornMaterial must be at $commit (tag $tag); found $head in '$Path'."
    }

    $expectedTree = Get-LocalizedTree $Path
    if (-not (Invoke-Git -C $Path status --porcelain=v1 --untracked-files=all)) {
        Invoke-Git -C $Path apply --index -- $localizationPatch
    }

    if ((Invoke-Git -C $Path write-tree) -cne $expectedTree -or
            (Invoke-Git -C $Path diff --name-only) -or
            (Invoke-Git -C $Path ls-files --others --exclude-standard)) {
        throw "RebornMaterial sources in '$Path' differ from the pinned source with the mainland translation applied. Move that checkout aside before retrying."
    }
}

if (Test-Path -LiteralPath $destination) {
    Initialize-RebornMaterialSource $destination
} else {
    New-Item -ItemType Directory -Force -Path $vendor | Out-Null
    $staging = Join-Path $vendor ('.RebornMaterial-' + [guid]::NewGuid().ToString('N'))
    try {
        Invoke-Git clone --quiet --depth 1 --single-branch --branch $tag --config core.autocrlf=false -- $url $staging
        Initialize-RebornMaterialSource $staging
        [System.IO.Directory]::Move($staging, $destination)
    } finally {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
}

Write-Output "RebornMaterial $tag ($commit) is ready at '$destination'."
