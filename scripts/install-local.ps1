# Builds the tool from this repo and installs it over the one you have, then starts it.
# For working on ancosSantiye itself; everyone else updates with `ancos-santiye update` or the page.
# Usage (from the repo folder):  ./scripts/install-local.ps1  [any server options, e.g. --profile <file>]
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

Write-Host 'Stopping a running ancosSantiye...'
Get-Process ancos-santiye -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host 'Packing...'
Remove-Item -Recurse -Force src/AncosSantiye/obj/Release, artifacts -ErrorAction SilentlyContinue
dotnet pack src/AncosSantiye -c Release -o artifacts --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed' }

# The same version number can be installed again only after an uninstall, and NuGet's cached
# copy of that version must go too, or it installs the old files again.
Write-Host 'Installing...'
dotnet tool uninstall -g AncosSantiye 2>$null | Out-Null
Remove-Item -Recurse -Force (Join-Path $env:USERPROFILE '.nuget/packages/ancossantiye') -ErrorAction SilentlyContinue
dotnet tool install -g AncosSantiye --add-source artifacts --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool install failed' }

Write-Host "Installed $(ancos-santiye version). Starting it; stop with Ctrl+C."
ancos-santiye @args
