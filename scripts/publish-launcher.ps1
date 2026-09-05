[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$')]
    [string]$Version = '0.1.2',
    [string]$DotnetPath = 'dotnet',
    [string]$PythonPath = 'python'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspacePrefix = $workspacePath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

function Get-WorkspacePath([string]$RelativePath) {
    $resolvedPath = [IO.Path]::GetFullPath((Join-Path $workspacePath $RelativePath))
    if (-not $resolvedPath.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output is outside the workspace: $resolvedPath"
    }
    $ancestorPath = $resolvedPath
    while ($ancestorPath -and $ancestorPath -ne $workspacePath) {
        if (Test-Path -LiteralPath $ancestorPath) {
            $item = Get-Item -LiteralPath $ancestorPath -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Output may not pass through a link: $ancestorPath"
            }
        }
        $ancestorPath = [IO.Path]::GetDirectoryName($ancestorPath)
    }
    return $resolvedPath
}

$projectPath = Get-WorkspacePath 'src/GemmaLauncher.App/GemmaLauncher.App.csproj'
$packageName = "GemmaLauncher-$Version-win-x64"
$distributionPath = Get-WorkspacePath 'dist'
$packagePath = Get-WorkspacePath "dist/$packageName"
$archivePath = Get-WorkspacePath "dist/$packageName.zip"
$checksumPath = Get-WorkspacePath "dist/$packageName.zip.sha256"
foreach ($targetPath in @($packagePath, $archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $targetPath) {
        throw "Output already exists. Move the previous result or choose another version: $targetPath"
    }
}

# Validate embedded translations before producing any release files.
& $PythonPath (Get-WorkspacePath 'scripts/check-locales.py')
if ($LASTEXITCODE -ne 0) { throw "Translation validation failed with exit code $LASTEXITCODE." }

$stagingRelativePath = 'dist/.publish-' + [Guid]::NewGuid().ToString('N')
$stagingPath = Get-WorkspacePath $stagingRelativePath
$null = New-Item -ItemType Directory -Path $distributionPath -Force
$null = New-Item -ItemType Directory -Path $stagingPath

try {
    $publishArguments = @(
        'publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '--output', $stagingPath,
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false',
        "-p:Version=$Version"
    )
    & $DotnetPath @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $executablePath = Join-Path $stagingPath 'GemmaLauncher.exe'
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) { throw 'The published application is missing.' }

    $assetsPath = Join-Path $stagingPath 'Assets'
    $licensesPath = Join-Path $stagingPath 'licenses'
    foreach ($directoryPath in @($assetsPath, $licensesPath)) {
        $null = New-Item -ItemType Directory -Path $directoryPath -Force
    }
    Copy-Item -LiteralPath (Get-WorkspacePath 'src/GemmaLauncher.App/Assets/catalog.json') -Destination (Join-Path $assetsPath 'catalog.json')
    Copy-Item -LiteralPath (Get-WorkspacePath 'README.md') -Destination $stagingPath
    Copy-Item -LiteralPath (Get-WorkspacePath 'LICENSE') -Destination $stagingPath
    Copy-Item -LiteralPath (Get-WorkspacePath 'THIRD-PARTY-NOTICES.md') -Destination $stagingPath

    # Resolve the exact runtime packages used by this SDK, not a different installed runtime.
    $packArguments = @(
        'msbuild', $projectPath, '-nologo', '-target:ResolveFrameworkReferences',
        '-getItem:ResolvedRuntimePack', '-property:RuntimeIdentifier=win-x64',
        '-property:SelfContained=true', '-property:Configuration=Release'
    )
    $packOutput = & $DotnetPath @packArguments
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate the runtime license packages.' }
    $packResult = ($packOutput -join [Environment]::NewLine) | ConvertFrom-Json
    $runtimePacks = @($packResult.Items.ResolvedRuntimePack)
    if ($runtimePacks.Count -lt 2) { throw 'The .NET and Windows Desktop runtime packages were not both resolved.' }
    foreach ($runtimePack in $runtimePacks) {
        $licenseDirectory = Join-Path $licensesPath $runtimePack.Identity
        $null = New-Item -ItemType Directory -Path $licenseDirectory -Force
        # Windows Desktop uses LICENSE without an extension; the core runtime uses LICENSE.TXT.
        $noticeFiles = @(Get-ChildItem -LiteralPath $runtimePack.PackageDirectory -File |
            Where-Object { $_.Name -match '^(LICENSE|THIRD[-_]?PARTY[-_]?NOTICES)(\.(TXT|MD))?$' })
        $licenseFiles = @($noticeFiles | Where-Object { $_.Name -match '^LICENSE(\.(TXT|MD))?$' })
        if ($licenseFiles.Count -eq 0) { throw "Runtime license is missing: $($runtimePack.Identity)" }
        foreach ($noticeFile in $noticeFiles) {
            Copy-Item -LiteralPath $noticeFile.FullName -Destination $licenseDirectory
        }
    }

    # Both paths are checked inside this workspace; existing releases are never removed.
    $stagingPath = Get-WorkspacePath $stagingRelativePath
    $packagePath = Get-WorkspacePath "dist/$packageName"
    Move-Item -LiteralPath $stagingPath -Destination $packagePath
    Compress-Archive -LiteralPath $packagePath -DestinationPath $archivePath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksumPath, "$hash  $packageName.zip" + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output "Published: $packagePath"
    Write-Output "Archive: $archivePath"
    Write-Output "SHA-256: $hash"
}
catch {
    Write-Warning "The incomplete build is preserved for inspection: $stagingPath"
    throw
}
