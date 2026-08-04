$ErrorActionPreference = "SilentlyContinue"
$revitFolders = Get-ChildItem "F:\reivt\2020" -Directory
foreach ($folder in $revitFolders) {
    if ($folder.Name -like "Revit*") {
        $apiDll = Join-Path $folder.FullName "RevitAPI.dll"
        $apiuiDll = Join-Path $folder.FullName "RevitAPIUI.dll"
        if (Test-Path $apiDll) { Write-Host "Found RevitAPI.dll in $($folder.FullName)" }
        if (Test-Path $apiuiDll) { Write-Host "Found RevitAPIUI.dll in $($folder.FullName)" }
    }
}
