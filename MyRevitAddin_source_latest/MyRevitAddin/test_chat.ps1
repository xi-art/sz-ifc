$key = 'sk-ws-H.RYXLEEH.paWp.MEQCIAYUxrhsOXrQuPLRRR41C1U_ZvLz-Bo0FEb2hdcwqT8RAiApcrTkM3pD3WZZTDnw84amFjN5VScjb3KcdIw99BM0Fw'
$url = 'https://llm-dmejvvfuvhvcokxf.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/chat/completions'
$headers = @{
    'Content-Type'  = 'application/json'
    'Authorization' = 'Bearer ' + $key
}
$body = @{
    model    = 'qwen-turbo'
    messages = @(
        @{ role = 'system'; content = '你是一个助手，用一句话回答' }
        @{ role = 'user'; content = '你好' }
    )
    max_tokens = 200
} | ConvertTo-Json -Depth 5
try {
    $r = Invoke-RestMethod -Uri $url -Headers $headers -Method Post -Body $body -TimeoutSec 30
    $r | ConvertTo-Json -Depth 8
} catch {
    'ERROR: ' + $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.ReadToEnd()
    }
}
