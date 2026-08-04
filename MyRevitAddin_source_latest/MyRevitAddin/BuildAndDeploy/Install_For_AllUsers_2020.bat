@echo off
chcp 65001 >nul
SETLOCAL EnableExtensions

REM ===== Revit 2020 用户级一键安装脚本 =====
REM 用法：关闭所有 Revit 2020 后双击运行

set "SCRIPT_DIR=%~dp0"
set "TARGET_DIR=%APPDATA%\Autodesk\Revit\Addins\2020"
set "TARGET_DIR_PROGRAMDATA=%PROGRAMDATA%\Autodesk\Revit\Addins\2020"

echo ============================================================
echo   MyRevitAddin - Revit 2020 安装（兮的 Revit 插件集）
echo ============================================================
echo.

REM 检查 DLL 是否在脚本同目录
if not exist "%SCRIPT_DIR%MyRevitAddin.dll" (
    echo [ERROR] 未找到 MyRevitAddin.dll，请将本脚本与以下文件放在同一目录：
    echo   - MyRevitAddin.dll
    echo   - MyRevitAddin.pdb
    echo   - MyRevitAddin.addin
    echo.
    pause
    exit /b 1
)

REM 检查 Revit 是否还开着
tasklist /FI "IMAGENAME eq Revit.exe" /FO TABLE | findstr /I /C:"Revit.exe" >nul 2>&1
if %ERRORLEVEL%==0 (
    echo [WARN] 检测到 Revit 进程正在运行！
    echo   请先完全关闭所有 Revit 窗口后再运行本脚本，否则 DLL 无法覆盖将导致更新失败。
    echo.
    <nul set /p "=仍然继续（会跳过占用的文件）？输入 Y 继续，其他键退出："
    set /p "ANS="
    if /I not "%ANS%"=="Y" (
        echo 已取消。
        pause
        exit /b 0
    )
)

REM 创建目录并复制到 用户 Addins 2020
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%" >nul 2>&1
echo.
echo [COPY→当前用户] %TARGET_DIR%
copy /Y "%SCRIPT_DIR%MyRevitAddin.dll"   "%TARGET_DIR%\"
copy /Y "%SCRIPT_DIR%MyRevitAddin.pdb"   "%TARGET_DIR%\"
copy /Y "%SCRIPT_DIR%MyRevitAddin.addin" "%TARGET_DIR%\"

REM 额外再复制一份到 所有用户 Addins 2020（可选，需管理员权限；失败忽略）
if not exist "%TARGET_DIR_PROGRAMDATA%" mkdir "%TARGET_DIR_PROGRAMDATA%" >nul 2>&1
echo [COPY→所有用户] %TARGET_DIR_PROGRAMDATA%
copy /Y "%SCRIPT_DIR%MyRevitAddin.dll"   "%TARGET_DIR_PROGRAMDATA%\" >nul 2>&1
copy /Y "%SCRIPT_DIR%MyRevitAddin.pdb"   "%TARGET_DIR_PROGRAMDATA%\" >nul 2>&1
copy /Y "%SCRIPT_DIR%MyRevitAddin.addin" "%TARGET_DIR_PROGRAMDATA%\" >nul 2>&1

echo.
echo ============================================================
echo  安装完成。现在可以打开 Revit 2020，
echo  功能区会出现「我的工具」选项卡（14 面板 30+ 按钮）。
echo ============================================================
pause
ENDLOCAL
