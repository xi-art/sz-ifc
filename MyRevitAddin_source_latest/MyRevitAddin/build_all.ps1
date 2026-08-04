# MyRevitAddin - 一键双版本编译部署（含重新归属标高插件）
# Revit 2018 + Revit 2020
# 用法: powershell -ExecutionPolicy Bypass -File F:\vs\code\MyRevitAddin\build_all.ps1

$projectDir = "F:\vs\code\MyRevitAddin"
$msbuild = "F:\vs\p\MSBuild\Current\Bin\MSBuild.exe"

function Build-And-Deploy {
    param($version, $csprojName)

    $targetCsproj = Join-Path $projectDir "MyRevitAddin.csproj"
    $csprojPath = Join-Path $projectDir $csprojName

    Write-Host "=============================="
    Write-Host "Building Revit $version from $csprojName..."
    Write-Host "=============================="

    # Switch csproj
    Copy-Item $csprojPath $targetCsproj -Force

    # Build
    $output = & $msbuild $targetCsproj /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal /t:Rebuild 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED for $version"
        $output | Select-String "error|Error" | ForEach-Object { Write-Host $_ }
        return $false
    }
    Write-Host "BUILD OK"

    # Deploy DLL (2018 has TWO DLL locations!)
    $dll = Join-Path $projectDir "bin\Debug\MyRevitAddin.dll"
    if ($version -eq "2018") {
        $loc1 = "C:\Users\Administrator\RevitAddins"
        $loc2 = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2018"
        Copy-Item $dll $loc1 -Force; $s1 = (Get-ChildItem (Join-Path $loc1 "MyRevitAddin.dll")).Length; Write-Host "  -> $loc1 ($s1 bytes)"
        Copy-Item $dll $loc2 -Force; $s2 = (Get-ChildItem (Join-Path $loc2 "MyRevitAddin.dll")).Length; Write-Host "  -> $loc2 ($s2 bytes)"
    } else {
        $addinDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2020"
        Copy-Item $dll $addinDir -Force
        $deployed = Get-ChildItem (Join-Path $addinDir "MyRevitAddin.dll")
        Write-Host "  -> $addinDir ($($deployed.Length) bytes)"
    }
    return $true
}

Write-Host "========================================"
Write-Host "MyRevitAddin - Build All ($($myInvocation.MyCommand.Path))"
Write-Host "========================================"

$ok2018 = Build-And-Deploy "2018" "MyRevitAddin_2018.csproj"
$ok2020 = Build-And-Deploy "2020" "MyRevitAddin_2020.csproj"

# Restore default csproj = 2020
Copy-Item (Join-Path $projectDir "MyRevitAddin_2020.csproj") (Join-Path $projectDir "MyRevitAddin.csproj") -Force

Write-Host "`n========================================"
Write-Host "SUMMARY"
Write-Host "========================================"
Write-Host "  Revit 2018: $(if($ok2018){'SUCCESS'}else{'FAILED'})"
Write-Host "  Revit 2020: $(if($ok2020){'SUCCESS'}else{'FAILED'})"
Write-Host "  Default csproj: 2020"
Write-Host "Done."
