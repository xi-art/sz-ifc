# Find MSBuild
$paths = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2026\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2026\Preview\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe",
    "C:\Program Files\dotnet\dotnet.exe"
)
foreach ($p in $paths) {
    if (Test-Path $p) {
        Write-Host "FOUND: $p"
    }
}
# Also try where.exe
$msbuild = (Get-Command msbuild -ErrorAction SilentlyContinue)
if ($msbuild) { Write-Host "where: $($msbuild.Source)" }
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if ($dotnet) { Write-Host "dotnet: $($dotnet.Source)" }
