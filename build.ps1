param([switch]$Verify, [string]$JavaHome = $env:PZ_JAVA_HOME, [string]$OutputDirectory = '', [switch]$Light)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'scripts/build-brand.ps1')
$projectPath = Join-Path $PSScriptRoot 'PZLauncher/PZLauncher/PZLauncher.csproj'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot $(if ($Light) { 'dist-light' } else { 'dist' }) }
$buildOutputPath = Join-Path $PSScriptRoot $(if ($Light) { 'artifacts/publish-build-light/' } else { 'artifacts/publish-build/' })
& (Join-Path $PSScriptRoot 'java-loader/build.ps1') -JavaHome $JavaHome
$selfContained = if ($Light) { 'false' } else { 'true' }
$compressBundle = if ($Light) { 'false' } else { 'true' }
& dotnet publish $projectPath -c Release -r win-x64 --self-contained $selfContained --nologo -v minimal `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    "-p:EnableCompressionInSingleFile=$compressBundle" -p:DebugType=None -p:DebugSymbols=false `
    "-p:BaseOutputPath=$buildOutputPath" -o $outputPath
if ($LASTEXITCODE -ne 0) { throw 'La publication du launcher a échoué.' }
& (Join-Path $PSScriptRoot 'scripts/publish-docs.ps1') -OutputDirectory $outputPath -Light:$Light
if ($Verify) {
    $process = Start-Process -FilePath (Join-Path $outputPath 'PZLauncher.exe') -ArgumentList '--verify' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw 'La vérification a échoué. Consultez %LocalAppData%/PZLauncher/verification.json.' }
}
Write-Output (Join-Path $outputPath 'PZLauncher.exe')
