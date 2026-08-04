@echo off
REM Run DashScope/AI assistant connection diagnostic from PowerShell
REM No need to set execution policy globally; use Bypass for this run only.
chcp 65001 > nul
cd /d "%~dp0"
echo ============================================
echo   MyRevitAddin - AI 助手连接诊断
echo   (Revit 外部运行版本，三层探测)
echo ============================================
echo.
PowerShell -NoProfile -ExecutionPolicy Bypass -File "%~dp0test_dashscope_conn.ps1"
echo.
pause
