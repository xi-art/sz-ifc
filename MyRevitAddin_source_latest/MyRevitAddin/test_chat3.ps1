$key = 'sk-ws-H.RYXLEEH.paWp.MEQCIAYUxrhsOXrQuPLRRR41C1U_ZvLz-Bo0FEb2hdcwqT8RAiApcrTkM3pD3WZZTDnw84amFjN5VScjb3KcdIw99BM0Fw'
$url = 'https://llm-dmejvvfuvhvcokxf.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/chat/completions'
$headers = @{
    'Content-Type'  = 'application/json'
    'Authorization' = 'Bearer ' + $key
}

# 测试 1: qwen-turbo + tools + tool_choice + temperature（完全模拟 ChatAsync）
$msgList = New-Object System.Collections.Generic.List[object]
$msgList.Add(@{ role = 'system'; content = '你是助手' })
$msgList.Add(@{ role = 'user'; content = '你好' })
$toolsList = New-Object System.Collections.Generic.List[object]
$toolsList.Add(@{
    type     = 'function'
    function = @{
        name        = 'get_revit_elements'
        description = '获取'
        parameters  = @{ type = 'object'; properties = @{ count = @{ type = 'integer' } } }
    }
})
$body1 = @{
    model       = 'qwen-turbo'
    messages    = $msgList
    tools       = $toolsList
    tool_choice = 'auto'
    temperature = 0.1
} | ConvertTo-Json -Depth 10

Write-Host 'BODY1: ' $body1
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
