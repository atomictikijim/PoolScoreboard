#Requires -Version 5.1
<#
.SYNOPSIS
    Checks a release tag against the version the build stamps onto every project.

.DESCRIPTION
    CLAUDE.md's version policy says the version in Directory.Build.props stays in step with the
    v0.<major>.<ui>-beta tags, and until now nothing checked it: a tag typed one number off
    published a download whose title, file properties and tag disagreed. The release workflow runs
    this before it publishes anything, so a mismatched tag is a red run rather than a release.

    A tag may carry a suffix (the -beta the policy uses, or a later -rc.2); the suffix is ignored
    because it is not part of VersionPrefix. Anything that is not
    v<major>.<minor>.<patch>[-suffix] is refused rather than parsed loosely, because working out
    what a tag probably meant is how a release ends up named a version nobody chose.

.PARAMETER Tag
    The tag being released, exactly as pushed: v0.43.0-beta.

.PARAMETER PropsPath
    The props file holding VersionPrefix. Defaults to the repository's Directory.Build.props, so a
    local run needs no argument beyond the tag.

.EXAMPLE
    ./tools/check-release-tag.ps1 -Tag v0.43.0-beta
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,

    [string] $PropsPath = ''
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not filled in yet while the parameters bind, so the default is worked out here.
if (-not $PropsPath) {
    $PropsPath = Join-Path $PSScriptRoot '..\Directory.Build.props'
}

# The ::error:: prefix is what turns this into a line on the run's summary page; run by hand it is
# just part of the message.
if ($Tag -notmatch '^v(?<version>\d+\.\d+\.\d+)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    Write-Host "::error::'$Tag' is not a release tag. Use v<major>.<minor>.<patch>, with an optional suffix such as -beta."
    exit 1
}
$tagged = $Matches['version']

$resolved = (Resolve-Path -LiteralPath $PropsPath).Path
$props = [xml](Get-Content -LiteralPath $resolved -Raw)
$prefix = ($props.Project.PropertyGroup | Where-Object { $_.VersionPrefix } | Select-Object -First 1).VersionPrefix

if (-not $prefix) {
    Write-Host "::error::No VersionPrefix found in $resolved."
    exit 1
}

if ($prefix -ne $tagged) {
    Write-Host "::error::Tag '$Tag' names version $tagged, but $resolved stamps VersionPrefix $prefix. Bump VersionPrefix in the commit that earns the version, then tag that commit."
    exit 1
}

Write-Host "Tag '$Tag' matches VersionPrefix $prefix."
exit 0
