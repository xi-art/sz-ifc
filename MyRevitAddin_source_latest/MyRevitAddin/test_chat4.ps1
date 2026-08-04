$key = 'sk-ws-H.RYXLEEH.paWp.MEQCIAYUxrhsOXrQuPLRRR41C1U_ZvLz-Bo0FEb2hdcwqT8RAiApcrTkM3pD3WZZTDnw84amFjN5VScjb3KcdIw99BM0Fw'
$url = 'https://llm-dmejvvfuvhvcokxf.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/chat/completions'
$headers = @{
    'Content-Type'  = 'application/json'
    'Authorization' = 'Bearer ' + $key
}

# 直接写完整的 JSON（用 \u 转义避免编码问题）
$body1 = @'
{"model":"qwen-turbo","messages":[{"role":"system","content":"你是助手"},{"role":"user","content":"你好"}],"tools":[{"type":"function","function":{"name":"get_revit_elements","description":"get elements","parameters":{"type":"object","properties":{"count":{"type":"integer"}}}}}],"tool_choice":"auto","temperature":0.1}
'@

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
