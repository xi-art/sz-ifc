@echo off
chcp 65001 >nul
echo ============================================
echo    编译一键部署工具
echo ============================================
echo.

set CSFILE=F:\vs\code\MyRevitAddin\BuildAndDeploy\BuildAndDeploy.cs
set OUTPUT=F:\vs\code\MyRevitAddin\BuildAndDeploy\BuildAndDeploy.exe
set NETFRAMEWORK=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319

echo 源文件: %CSFILE%
echo 输出: %OUTPUT%
echo.

if not exist "%NETFRAMEWORK%\csc.exe" (
    echo 错误: csc.exe 不存在
    pause
    exit /b 1
)

echo 正在编译...
"%NETFRAMEWORK%\csc.exe" /target:winexe /out:"%OUTPUT%" /platform:x86 /reference:System.Windows.Forms.dll /reference:System.Data.dll "%CSFILE%"

if errorlevel 1 (
    echo.
    echo 编译失败！
    pause
    exit /b 1
)

echo.
echo 编译成功！
echo 输出文件: %OUTPUT%
echo.
echo 双击 BuildAndDeploy.exe 即可使用
pause