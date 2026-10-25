param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$buildRoot = Join-Path $repositoryRoot 'artifacts/bear-animation-review'
$production = Join-Path $repositoryRoot 'artifacts/bear-natural/build/bin/DesktopPet.App/debug'
if (-not (Test-Path -LiteralPath (Join-Path $production 'DesktopPet.dll'))) { throw 'Build the current bear application first (see STATUS).' }
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
$escapedProduction = [System.Security.SecurityElement]::Escape($production)
$manifest = [System.Security.SecurityElement]::Escape((Join-Path $repositoryRoot 'src/DesktopPet.App/app.manifest'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF><UseWindowsForms>true</UseWindowsForms><NoWarn>WFO0003</NoWarn>
    <ApplicationManifest>$manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="DesktopPet"><HintPath>$escapedProduction\DesktopPet.dll</HintPath></Reference>
    <Reference Include="DesktopPet.Core"><HintPath>$escapedProduction\DesktopPet.Core.dll</HintPath></Reference>
    <None Include="$escapedProduction\assets\**\*" Link="assets\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $buildRoot 'BearAnimationReview.csproj'), $project)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BearAnimationReview.cs') -Destination $buildRoot
dotnet build (Join-Path $buildRoot 'BearAnimationReview.csproj') -c Debug -m:1 --ignore-failed-sources --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Review host build failed.' }
Write-Output (Join-Path $buildRoot 'bin/Debug/net10.0-windows/BearAnimationReview.exe')
