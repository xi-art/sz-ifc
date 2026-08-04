# MyRevitAddin 构建脚本（Revit 2020）
# 用法: 右键 -> 用 PowerShell 运行

$ErrorActionPreference = "Stop"
$Project = "F:\vs\code\MyRevitAddin\MyRevitAddin.csproj"
$Output = "C:\Users\Administrator\AppData\Roaming\Autodesk\Revit\Addins\2020\MyRevitAddin.dll"
$AddonDir = "C:\Users\Administrator\AppData\Roaming\Autodesk\Revit\Addins\2020"

Write-Host "========== MyRevitAddin 构建脚本 =========="
Write-Host "目标: Revit 2020"
Write-Host ""

# 编译
Write-Host "[1/3] 编译项目..."
dotnet build $Project -v minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] 编译失败" -ForegroundColor Red
    exit 1
}

# 复制 DLL
Write-Host "[2/3] 复制到 Revit 2020 加载目录..."
if (-not (Test-Path $AddonDir)) {
    New-Item -ItemType Directory -Path $AddonDir -Force | Out-Null
}
Copy-Item "$Project\..\bin\Debug\MyRevitAddin.dll" $Output -Force
Write-Host "      -> $Output" -ForegroundColor Green

# 确认
Write-Host "[3/3] 验证 .addin 注册..."
if (Test-Path "$AddonDir\MyRevitAddin.addin") {
    Write-Host "      .addin 文件已就绪" -ForegroundColor Green
} else {
    Write-Host "      [WARNING] .addin 文件不存在，请手动创建" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========== 构建完成 =========="
Write-Host "请在 Revit 2020 中点击 '我的工具' 选项卡测试插件"
Write-Host "  - 批量复制图纸：批量复制图纸与视图（参照 SmartViews）"
Write-Host "  - 关键标记避障：批量选中关键标记自动平移避让"
Write-Host ""
