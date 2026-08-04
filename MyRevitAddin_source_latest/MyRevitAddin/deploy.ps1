# deploy.ps1 - 全量部署：更新 .addin + 复制 DLL 到 AppData
$addin2020 = "C:\Users\Administrator\AppData\Roaming\Autodesk\Revit\Addins\2020\MyRevitAddin.addin"
$addin2018 = "C:\Users\Administrator\AppData\Roaming\Autodesk\Revit\Addins\2018\MyRevitAddin.addin"
$dll2020   = "F:\vs\code\MyRevitAddin\bin\Debug\MyRevitAddin.dll"
$dll2018   = "F:\vs\code\MyRevitAddin\bin\Debug_2018\MyRevitAddin.dll"
$dest2020  = "C:\Users\Administrator\AppData\Roaming\Autodesk\Revit\Addins\2020\MyRevitAddin.dll"
$dest2018  = "C:\Users\Administrator\AppData\Roaming\Autodesk\Revit\Addins\2018\MyRevitAddin.dll"

function Deploy-One($name, $dll, $addin, $dest) {
    if (-not (Test-Path $dll)) {
        Write-Host "MISSING DLL: $dll  (run build script first)"
        return $false
    }
    $info = Get-Item $dll
    Copy-Item -Force $dll $dest
    $dllOut = Get-Item $dest
    Write-Host ("  [{0}] {1} bytes  {2}" -f $name, $dllOut.Length, $dllOut.LastWriteTime.ToString("HH:mm:ss"))

    $addinXml = "<?xml version=""1.0"" encoding=""utf-8"" ?>" + [Environment]::NewLine
    $addinXml = $addinXml + "<RevitAddIns>" + [Environment]::NewLine
    $addinXml = $addinXml + "  <AddIn Type=""Application"">" + [Environment]::NewLine
    $addinXml = $addinXml + "    <Name>MyRevitAddin</Name>" + [Environment]::NewLine
    $addinXml = $addinXml + "    <Assembly>$dest</Assembly>" + [Environment]::NewLine
    $addinXml = $addinXml + "    <FullClassName>MyRevitAddin.App</FullClassName>" + [Environment]::NewLine
    $addinXml = $addinXml + "    <ClientId>41C5AEE7-36B4-4FE1-919D-22D3D9DFF776</ClientId>" + [Environment]::NewLine
    $addinXml = $addinXml + "    <VendorId>MyRevitAddin</VendorId>" + [Environment]::NewLine
    $addinXml = $addinXml + "    <VendorDescription>MyRevitAddin for Revit $name</VendorDescription>" + [Environment]::NewLine
    $addinXml = $addinXml + "  </AddIn>" + [Environment]::NewLine
    $addinXml = $addinXml + "</RevitAddIns>"
    $addinXml | Set-Content -Path $addin -Encoding UTF8
    Write-Host ("  [addin] updated  -> $addin")
    return $true
}

Write-Host "Deploying MyRevitAddin..."
$ok = $true
if (-not (Deploy-One "2020" $dll2020 $addin2020 $dest2020)) { $ok = $false }
if (-not (Deploy-One "2018" $dll2018 $addin2018 $dest2018)) { $ok = $false }

if ($ok) {
    Write-Host "All deployed. Restart Revit to load."
} else {
    Write-Host "Some targets missing."
    exit 1
}
