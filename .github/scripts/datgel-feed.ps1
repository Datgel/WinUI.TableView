# DH-2508 - helpers for .github/workflows/datgel-publish.yml (dot-source it).
# Talks to the datgel-github NuGet feed with Basic auth FEED_USER:FEED_TOKEN.
# Needs env: FEED_URL, FEED_USER, FEED_TOKEN, PACKAGE_ID.

# NuGet's normalised form, as written into the nuspec and served by the feed: a 4th part
# of 0 is dropped (1.5.0.0 -> 1.5.0, 1.5.0.0-x -> 1.5.0-x), lower-case. Measured on the first
# dry run of this workflow, which packed 1.5.0.0-dryrun.1 and got nuspec version 1.5.0-dryrun.1.
function ConvertTo-NuGetVersion([string] $Version) {
    $v = $Version.Trim().ToLowerInvariant()
    if ($v -match '^(\d+\.\d+\.\d+)\.0+(-.*)?$') { $v = $Matches[1] + $Matches[2] }
    $v
}

function Get-FeedHeaders {
    if ([string]::IsNullOrEmpty($env:FEED_TOKEN)) { throw "FEED_TOKEN is empty." }
    $pair = "$($env:FEED_USER):$($env:FEED_TOKEN)"
    @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair)) }
}

function Get-FeedBaseAddress {
    $index = Invoke-RestMethod -Uri $env:FEED_URL -Headers (Get-FeedHeaders)
    $base = ($index.resources | Where-Object { $_.'@type' -eq 'PackageBaseAddress/3.0.0' } | Select-Object -First 1).'@id'
    if (-not $base) { throw "The feed's service index has no PackageBaseAddress/3.0.0." }
    $base.TrimEnd('/')
}

# Every version of PACKAGE_ID on the feed, lower-case (flat-container form).
function Get-FeedVersions {
    $id = $env:PACKAGE_ID.ToLowerInvariant()
    try {
        $r = Invoke-RestMethod -Uri "$(Get-FeedBaseAddress)/$id/index.json" -Headers (Get-FeedHeaders)
    } catch {
        throw "Could not list $($env:PACKAGE_ID) on the feed: $($_.Exception.Message). A 401/403 means this token cannot READ the package."
    }
    @($r.versions | ForEach-Object { $_.ToLowerInvariant() })
}

function Save-FeedPackage([string] $Version, [string] $OutFile) {
    $id = $env:PACKAGE_ID.ToLowerInvariant()
    $v = ConvertTo-NuGetVersion $Version
    # The feed redirects to signed blob storage; PowerShell 7 drops Authorization on the cross-host hop.
    Invoke-WebRequest -Uri "$(Get-FeedBaseAddress)/$id/$v/$id.$v.nupkg" -Headers (Get-FeedHeaders) -OutFile $OutFile
}

# id / version / repository / lib file list / normalised dependency groups of a .nupkg.
function Read-Nupkg([string] $Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path))
    try {
        $lib = @($zip.Entries | Where-Object { $_.FullName -like 'lib/*' -and $_.Name } | ForEach-Object { $_.FullName } | Sort-Object)
        $entry = $zip.Entries | Where-Object { $_.FullName -notlike '*/*' -and $_.Name -like '*.nuspec' } | Select-Object -First 1
        if (-not $entry) { throw "$Path has no root .nuspec." }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    $meta = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    $repo = $meta.SelectSingleNode("*[local-name()='repository']")
    $deps = $meta.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']") | ForEach-Object {
        $g = $_.GetAttribute('targetFramework')
        $d = $_.SelectNodes("*[local-name()='dependency']") | ForEach-Object { "$($_.GetAttribute('id')) $($_.GetAttribute('version')) [$($_.GetAttribute('exclude'))]" } | Sort-Object
        "$g :: $($d -join '; ')"
    } | Sort-Object
    [pscustomobject]@{
        Id               = $meta.SelectSingleNode("*[local-name()='id']").InnerText
        Version          = $meta.SelectSingleNode("*[local-name()='version']").InnerText
        RepositoryUrl    = if ($repo) { $repo.GetAttribute('url') } else { $null }
        RepositoryCommit = if ($repo) { $repo.GetAttribute('commit') } else { $null }
        Lib              = $lib
        Dependencies     = ($deps -join "`n")
    }
}
