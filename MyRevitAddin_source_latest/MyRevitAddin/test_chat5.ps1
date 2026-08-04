$key = 'sk-ws-H.RYXLEEH.paWp.MEQCIAYUxrhsOXrQuPLRRR41C1U_ZvLz-Bo0FEb2hdcwqT8RAiApcrTkM3pD3WZZTDnw84amFjN5VScjb3KcdIw99BM0Fw'
$url = 'https://llm-dmejvvfuvhvcokxf.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/chat/completions'
$headers = @{
    'Content-Type'  = 'application/json'
    'Authorization' = 'Bearer ' + $key
}

$body1 = Get-Content -Raw 'F:\vs\code\MyRevitAddin\body1.json'

Write-Host 'SENDING BODY1:'
Write-Host $body1
Write-Host '---'

try {
    $r1 = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body1 -TimeoutSec 30
    Write-Host 'TEST1 OK:'
    $r1 | ConvertTo-Json -Depth 8
} catch {
    Write-Host 'TEST1 ERROR:' $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.ReadToEnd()
    }
}
