# build_2020.ps1 - Revit 2020 一键编译脚本（Trea 友好）
# 用法：在 Trea / PowerShell 中执行 build_2020.ps1
$msbuild = "F:\vs\p\MSBuild\Current\Bin\MSBuild.exe"
$proj    = "F:\vs\code\MyRevitAddin\MyRevitAddin_2020.csproj"
$dll     = "F:\vs\code\MyRevitAddin\bin\Debug\MyRevitAddin.dll"

if (-not (Test-Path $msbuild)) { Write-Host "MSBuild NOT FOUND: $msbuild"; exit 1 }
if (-not (Test-Path $proj))    { Write-Host "PROJECT NOT FOUND: $proj";    exit 1 }

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $msbuild $proj /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal /nologo
$sw.Stop()

if ($LASTEXITCODE -eq 0) {
    if (Test-Path $dll) {
        $info = Get-Item $dll
        Write-Host ("OK {0:0.1f}s | {1} | {2} bytes" -f $sw.Elapsed.TotalSeconds, $info.LastWriteTime.ToString("HH:mm:ss"), $info.Length)
    } else {
        Write-Host "OK but DLL not found at $dll"
    }
} else {
    Write-Host "FAIL exit=$LASTEXITCODE ($($sw.Elapsed.TotalSeconds)s)"
    exit $LASTEXITCODE
}
