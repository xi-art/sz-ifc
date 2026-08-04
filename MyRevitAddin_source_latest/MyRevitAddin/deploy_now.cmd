@echo off
chcp 65001 >nul
title MyRevitAddin - 一键部署（Revit 2018 / 2020）

echo ========================================
echo  MyRevitAddin 插件部署
echo  请先关闭所有 Revit 窗口再运行！
echo ========================================
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0deploy_now.ps1"
