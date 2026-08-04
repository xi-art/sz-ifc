$key = 'sk-ws-H.RYXLEEH.paWp.MEQCIAYUxrhsOXrQuPLRRR41C1U_ZvLz-Bo0FEb2hdcwqT8RAiApcrTkM3pD3WZZTDnw84amFjN5VScjb3KcdIw99BM0Fw'
$url = 'https://llm-dmejvvfuvhvcokxf.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/chat/completions'
$headers = @{
    'Content-Type'  = 'application/json'
    'Authorization' = 'Bearer ' + $key
}
# 模拟 ChatAsync 的请求结构（含 tools 和 tool_choice）
$body = @{
    model       = 'qwen-turbo'
    messages    = @(
        @{ role = 'system'; content = '你是助手' }
        @{ role = 'user'; content = '你好' }
    )
    tools       = @(
        @{
            type     = 'function'
            function = @{
                name        = 'get_revit_elements'
                description = '获取 Revit 元素'
                parameters  = @{
                    type       = 'object'
                    properties = @{ count = @{ type = 'integer' } }
                }
            }
        }
    )
    tool_choice = 'auto'
    temperature = 0.1
} | ConvertTo-Json -Depth 10
try {
    $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body -TimeoutSec 30
    'CHAT_WITH_TOOLS OK: ' + ($r.choices[0].message.content)
} catch {
    'CHAT_WITH_TOOLS ERROR: ' + $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.ReadToEnd()
    }
}

# 试 deepseek-v3
$body2 = @{
    model       = 'deepseek-v3'
    messages    = @( @{ role = 'user'; content = 'hi' } )
    max_tokens  = 50
} | ConvertTo-Json -Depth 5
try {
    $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body2 -TimeoutSec 30
    'DEEPSEEK OK: ' + ($r.choices[0].message.content)
} catch {
    'DEEPSEEK ERROR: ' + $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.ReadToEnd()
    }
}
