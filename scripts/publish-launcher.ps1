[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$')]
    [string]$Version = '0.1.3',
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
$executableName = "GemmaLauncher-$Version-win-x64.exe"
$distributionPath = Get-WorkspacePath 'dist'
$releasePath = Get-WorkspacePath "dist/$executableName"
$checksumPath = Get-WorkspacePath "dist/$executableName.sha256"
foreach ($targetPath in @($releasePath, $checksumPath)) {
    if (Test-Path -LiteralPath $targetPath) { throw "Output already exists: $targetPath" }
}

& $PythonPath (Get-WorkspacePath 'scripts/check-locales.py')
if ($LASTEXITCODE -ne 0) { throw "Translation validation failed with exit code $LASTEXITCODE." }
$catalogGenerator = Get-WorkspacePath 'scripts/create-product-catalog.py'
if (Test-Path -LiteralPath $catalogGenerator -PathType Leaf) {
    & $PythonPath $catalogGenerator --check
    if ($LASTEXITCODE -ne 0) { throw "Catalog validation failed with exit code $LASTEXITCODE." }
}

$stagingRelativePath = 'dist/.publish-' + [Guid]::NewGuid().ToString('N')
$stagingPath = Get-WorkspacePath $stagingRelativePath
$publishPath = Get-WorkspacePath "$stagingRelativePath/app"
$noticesPath = Get-WorkspacePath "$stagingRelativePath/Notices.txt"
$reportPath = Get-WorkspacePath "$stagingRelativePath/verification.json"
$null = New-Item -ItemType Directory -Path $distributionPath -Force
$null = New-Item -ItemType Directory -Path $publishPath

try {
    & $DotnetPath restore $projectPath -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    # Bundle notices from the exact runtime packages selected for this publish.
    $packArguments = @(
        'msbuild', $projectPath, '-nologo', '-target:ResolveFrameworkReferences',
        '-getItem:ResolvedRuntimePack', '-property:RuntimeIdentifier=win-x64',
        '-property:SelfContained=true', '-property:Configuration=Release'
    )
    $packOutput = & $DotnetPath @packArguments
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate the runtime license packages.' }
    $packResult = ($packOutput -join [Environment]::NewLine) | ConvertFrom-Json
    $runtimePacks = @($packResult.Items.ResolvedRuntimePack)
    foreach ($requiredPack in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        if ($requiredPack -notin $runtimePacks.Identity) { throw "Runtime package is missing: $requiredPack" }
    }
    $notices = [Text.StringBuilder]::new()
    foreach ($sourceName in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
        $null = $notices.AppendLine("[Launcher/$sourceName]")
        $null = $notices.AppendLine([IO.File]::ReadAllText((Get-WorkspacePath $sourceName)))
        $null = $notices.AppendLine()
    }
    foreach ($runtimePack in ($runtimePacks | Sort-Object Identity)) {
        $noticeFiles = @(Get-ChildItem -LiteralPath $runtimePack.PackageDirectory -File |
            Where-Object { $_.Name -match '^(LICENSE|THIRD[-_]?PARTY[-_]?NOTICES)(\.(TXT|MD))?$' } |
            Sort-Object Name)
        if (@($noticeFiles | Where-Object { $_.Name -match '^LICENSE(\.(TXT|MD))?$' }).Count -eq 0) {
            throw "Runtime license is missing: $($runtimePack.Identity)"
        }
        foreach ($noticeFile in $noticeFiles) {
            $null = $notices.AppendLine("[$($runtimePack.Identity)/$($noticeFile.Name)]")
            $null = $notices.AppendLine([IO.File]::ReadAllText($noticeFile.FullName))
            $null = $notices.AppendLine()
        }
    }
    [IO.File]::WriteAllText($noticesPath, $notices.ToString(), [Text.UTF8Encoding]::new($false))

    $publishArguments = @(
        'publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '--no-restore', '--output', $publishPath,
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true', '-p:PublishTrimmed=false',
        '-p:DebugType=None', '-p:DebugSymbols=false',
        "-p:LauncherNoticesFile=$noticesPath", "-p:Version=$Version"
    )
    & $DotnetPath @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $publishedFiles = @(Get-ChildItem -LiteralPath $publishPath -File -Recurse)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'GemmaLauncher.exe') {
        throw 'Single-file publish must produce only GemmaLauncher.exe.'
    }

    # Exercise this exact EXE from its otherwise empty directory, without a UI or server.
    $probe = Start-Process -FilePath $publishedFiles[0].FullName -ArgumentList @('--verify-package', ('"' + $reportPath + '"')) -WorkingDirectory $publishPath -WindowStyle Hidden -PassThru
    if (-not $probe.WaitForExit(30000)) {
        $probe.Kill()
        throw 'Single-file verification did not finish within 30 seconds.'
    }
    if ($probe.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw 'Single-file verification failed.'
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.success -ne $true -or $report.modelCount -ne 3 -or $report.languageCount -ne 17) {
        throw 'The single EXE does not contain the expected catalog and languages.'
    }

    # Only the verified EXE is distributed. Prior releases and inspection files are preserved.
    Move-Item -LiteralPath (Get-WorkspacePath "$stagingRelativePath/app/GemmaLauncher.exe") -Destination $releasePath
    $hash = (Get-FileHash -LiteralPath $releasePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksumPath, "$hash  $executableName" + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output "Executable: $releasePath"
    Write-Output "SHA-256: $hash"
}
catch {
    Write-Warning "The incomplete build is preserved for inspection: $stagingPath"
    throw
}
