# Builds the Microsoft Store package: dist\Atoll-<version>.msix (unsigned: the Store signs it).
#   powershell -File tools\make-msix.ps1            build the package
#   powershell -File tools\make-msix.ps1 -Register  also install the layout locally to test (needs Developer Mode)
param([switch]$Register)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

[xml]$proj = Get-Content DynamicIsland.csproj
$ver = ($proj.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
$pkgVer = "$ver.0"

# the tools (makeappx, makepri) come from Microsoft's NuGet package
$tools = Join-Path $root 'obj\msix-tools'
if (-not (Test-Path "$tools\tools.csproj")) {
  New-Item -ItemType Directory -Force $tools | Out-Null
  Set-Content "$tools\tools.csproj" '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4654" /></ItemGroup></Project>'
}
dotnet restore "$tools\tools.csproj" | Out-Null
$bin = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.4654\bin" -Recurse -Filter makeappx.exe | Where-Object FullName -like '*\x64\*' | Select-Object -First 1
$makeappx = $bin.FullName
$makepri = Join-Path $bin.DirectoryName 'makepri.exe'

$layout = Join-Path $root 'publish\msix'
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
dotnet publish DynamicIsland.csproj -c Release -r win-x64 --self-contained true -o $layout | Out-Null

(Get-Content packaging\msix\AppxManifest.xml -Raw).Replace('{VERSION}', $pkgVer) | Set-Content "$layout\AppxManifest.xml" -Encoding utf8
Copy-Item packaging\msix\Images "$layout\Images" -Recurse

Push-Location $layout
& $makepri createconfig /cf priconfig.xml /dq en-US /o | Out-Null
& $makepri new /pr . /cf priconfig.xml /mn AppxManifest.xml /of resources.pri /o | Out-Null
Remove-Item priconfig.xml
Pop-Location

New-Item -ItemType Directory -Force dist | Out-Null
$out = Join-Path $root "dist\Atoll-$ver.msix"
& $makeappx pack /d $layout /p $out /o | Select-Object -Last 2
Write-Output "package: $out ($pkgVer)"

if ($Register) {
  Add-AppxPackage -Register "$layout\AppxManifest.xml"
  Write-Output "registered locally"
}
