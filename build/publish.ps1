<#
 Builds, tests, publishes NOVA and (if Inno Setup is installed) the installer.
 Usage:  .\build\publish.ps1                 # framework-dependent (needs .NET 8 Desktop Runtime on the PC)
         .\build\publish.ps1 -SelfContained  # bundles the runtime (larger, no prerequisites)
#>
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $root 'artifacts\publish'
$sc   = if ($SelfContained) { 'true' } else { 'false' }
$tag  = if ($SelfContained) { 'selfcontained' } else { 'fxdependent' }

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet test (Join-Path $root 'Nova.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not publishing.' }

dotnet publish (Join-Path $root 'src\Nova.App\Nova.App.csproj') -c Release -r win-x64 --self-contained $sc -p:PublishReadyToRun=true -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$zip = Join-Path $root "artifacts\NOVA-portable-$tag.zip"
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force
Write-Host "Portable build: $zip"

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($iscc) { & $iscc.Source (Join-Path $root 'installer\NOVA.iss'); Write-Host 'Installer built in artifacts\' }
else { Write-Host 'Inno Setup (iscc.exe) not found; skipped installer. Install from https://jrsoftware.org/isinfo.php' }
