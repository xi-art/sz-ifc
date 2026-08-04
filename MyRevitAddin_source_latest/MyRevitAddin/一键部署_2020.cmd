@echo off
chcp 65001 >nul
echo ============================================
echo    MyRevitAddin 一键部署（Revit 2020）
echo ============================================
echo.

set SRC_DLL=F:\vs\code\MyRevitAddin\bin\Debug\MyRevitAddin.dll
set SRC_PDB=F:\vs\code\MyRevitAddin\bin\Debug\MyRevitAddin.pdb
set SRC_ADDIN=F:\vs\code\MyRevitAddin\deploy\2020\MyRevitAddin.addin
set DST_DIR=%APPDATA%\Autodesk\Revit\Addins\2020
set DST_DLL=%DST_DIR%\MyRevitAddin.dll
set DST_PDB=%DST_DIR%\MyRevitAddin.pdb
set DST_ADDIN=%DST_DIR%\MyRevitAddin.addin

echo 源文件：
echo   DLL: %SRC_DLL%
echo   ADDIN: %SRC_ADDIN%
echo.
echo 目标目录：
echo   %DST_DIR%
echo.

if not exist "%SRC_DLL%" (
    echo [错误] 源 DLL 不存在，请先编译项目。
    pause
    exit /b 1
)

if not exist "%DST_DIR%" (
    echo 目标目录不存在，创建中...
    mkdir "%DST_DIR%"
)

echo 正在检查 Revit 是否运行中...
tasklist /FI "IMAGENAME eq Revit.exe" /FO TABLE 2>nul | find /I "Revit.exe" >nul
if %ERRORLEVEL%==0 (
    echo.
    echo [警告] 检测到 Revit 正在运行！
    echo 请先完全退出 Revit，再运行本脚本。
    echo.
    pause
    exit /b 1
)
echo Revit 未运行，可以部署。
echo.

echo 复制 DLL...
copy /Y "%SRC_DLL%" "%DST_DLL%"
if errorlevel 1 (
    echo [错误] DLL 复制失败
    pause
    exit /b 1
)

echo 复制 PDB...
copy /Y "%SRC_PDB%" "%DST_PDB%" 2>nul

echo 复制 .addin 清单...
copy /Y "%SRC_ADDIN%" "%DST_ADDIN%"

echo.
echo ============================================
echo   部署完成！
echo ============================================
echo.
echo 部署文件：
dir /B "%DST_DIR%\MyRevitAddin*" 2>nul
echo.
echo 启动 Revit 2020 即可使用新版插件。
echo.
pause
