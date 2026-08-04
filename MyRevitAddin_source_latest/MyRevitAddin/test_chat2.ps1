$key = 'sk-ws-H.RYXLEEH.paWp.MEQCIAYUxrhsOXrQuPLRRR41C1U_ZvLz-Bo0FEb2hdcwqT8RAiApcrTkM3pD3WZZTDnw84amFjN5VScjb3KcdIw99BM0Fw'
$url = 'https://llm-dmejvvfuvhvcokxf.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/chat/completions'
$headers = @{
    'Content-Type'  = 'application/json'
    'Authorization' = 'Bearer ' + $key
}

# 测试 1: qwen-turbo + tools + temperature（模拟 ChatAsync）
$body1 = '{"model":"qwen-turbo","messages":[{"role":"system","content":"你是助手"},{"role":"user","content":"你好"}],"tools":[{"type":"function","function":{"name":"get_revit_elements","description":"获取","parameters":{"type":"object","properties":{"count":{"type":"integer"}}}}}],"tool_choice":"auto","temperature":0.1}'

try {
    $r1 = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body1 -TimeoutSec 30
    'TEST1 (qwen-turbo + tools) OK: ' + $r1.choices[0].message.content
} catch {
    'TEST1 ERROR: ' + $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.ReadToEnd()
    }
}

Write-Host '---'

# 测试 2: deepseek-v3 简单请求
$body2 = '{"model":"deepseek-v3","messages":[{"role":"user","content":"hi"}],"max_tokens":50}'
try {
    $r2 = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body2 -TimeoutSec 30
    'TEST2 (deepseek-v3) OK: ' + $r2.choices[0].message.content
} catch {
    'TEST2 ERROR: ' + $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.ReadToEnd()
    }
}

Write-Host '---'

# 测试 3: qwen-plus 简单请求
$body3 = '{"model":"qwen-plus","messages":[{"role":"user","content":"hi"}],"max_tokens":50}'
try {
    $r3 = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body3 -TimeoutSec 30
    'TEST3 (qwen-plus) OK: ' + $r3.choices[0].message.content
} catch {
    'TEST3 ERROR: ' + $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.ReadToEnd()
    }
}
