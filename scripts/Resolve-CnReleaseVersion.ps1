param(
    [string]$Commit = 'HEAD',
    [string]$UpstreamRepository = 'https://github.com/FFXIV-CombatReborn/GatherBuddyReborn.git'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Get-ReleaseTags([string]$Prefix, [string]$MergedInto) {
    $arguments = @('for-each-ref', '--format=%(refname)')
    if ($MergedInto) { $arguments += "--merged=$MergedInto" }
    foreach ($ref in (git @arguments $Prefix)) {
        $tag = $ref.Substring($Prefix.Length)
        [version]$version = $null
        if ([version]::TryParse($tag, [ref]$version) -and $version.Revision -ge 0 -and $version.ToString() -ceq $tag) {
            [pscustomobject]@{ Ref = $ref; Version = $version }
        }
    }
}

$commitId = git rev-parse --verify "$Commit^{commit}"
$upstreamRefs = 'refs/gbr-upstream-tags/'
git fetch --quiet --prune --no-tags $UpstreamRepository "+refs/tags/*:${upstreamRefs}*"
$upstream = Get-ReleaseTags $upstreamRefs $commitId | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $upstream) { throw "No upstream release tag is reachable from $commitId." }
$baseline = $upstream.Version

# Reserve two decimal digits for CN revisions. Every assembly version component must fit in 0..65534.
if ($baseline.Major -gt 65534 -or $baseline.Minor -gt 65534 -or $baseline.Build -gt 65534 -or $baseline.Revision -gt 654) {
    throw "Upstream version $baseline is outside the CN release numbering range."
}
$firstThree = $baseline.ToString(3)
$revisionBase = 100 * $baseline.Revision
$tags = @(Get-ReleaseTags 'refs/tags/' | Sort-Object Version -Descending)
$latest = $tags | Select-Object -First 1
if ($latest -and $latest.Ref -cnotin (git for-each-ref "--merged=$commitId" '--format=%(refname)' $latest.Ref)) {
    throw "The latest version $($latest.Version) is not included in $commitId. Refusing to release an older or divergent revision."
}

$lastInBaseline = $tags | Where-Object {
    $_.Version.ToString(3) -ceq $firstThree -and
    $_.Version.Revision -gt $revisionBase -and $_.Version.Revision -lt ($revisionBase + 100)
} | Select-Object -First 1

$revision = 1
if ($lastInBaseline) {
    $revision = $lastInBaseline.Version.Revision - $revisionBase
    # A retry after the tag was created must use the same version for this commit.
    if ((git rev-parse --verify "$($lastInBaseline.Ref)^{commit}") -cne $commitId) { $revision++ }
}
if ($revision -gt 99) { throw "All 99 CN revisions for upstream version $baseline have been used." }

$version = [version]"$firstThree.$($revisionBase + $revision)"
if ($latest -and $version -lt $latest.Version) {
    throw "Computed version $version would go backwards from $($latest.Version)."
}

[pscustomobject]@{
    Version = $version.ToString()
    Baseline = $baseline.ToString()
    Title = "$baseline-cn.$revision"
}
