@echo off
chcp 65001 >nul
set SRC_DLL=F:\vs\code\MyRevitAddin\bin\Debug\MyRevitAddin.dll
set SRC_PDB=F:\vs\code\MyRevitAddin\bin\Debug\MyRevitAddin.pdb
set SRC_ADDIN=F:\vs\code\MyRevitAddin\deploy\2020\MyRevitAddin.addin
set DST_DIR=%APPDATA%\Autodesk\Revit\Addins\2020

if not exist "%DST_DIR%" mkdir "%DST_DIR%"

echo COPY: DLL
copy /Y "%SRC_DLL%" "%DST_DIR%\"
echo COPY: PDB
copy /Y "%SRC_PDB%" "%DST_DIR%\"
echo COPY: ADDIN
copy /Y "%SRC_ADDIN%" "%DST_DIR%\"

echo.
echo ============================================================
echo  Done. Files now in: %DST_DIR%
echo ============================================================
dir /b "%DST_DIR%\MyRevitAddin.*"
