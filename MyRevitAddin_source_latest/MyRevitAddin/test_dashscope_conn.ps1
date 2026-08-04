param(
    [string]$ApiKey = "",
    [string]$Model  = "qwen-turbo",
    [string]$Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"
)

$ErrorActionPreference = "Continue"
$host.UI.RawUI.WindowTitle = "DashScope Connection Diagnostics"

function Write-Step($i, $t) { Write-Host ""; Write-Host ("[Step {0}/3] {1}" -f $i, $t) -ForegroundColor Cyan }
function Write-Pass($m) { Write-Host ("  PASS  "+$m) -ForegroundColor Green }
function Write-Fail($m) { Write-Host ("  FAIL  "+$m) -ForegroundColor Red }
function Write-Info($m) { Write-Host ("  INFO  "+$m) -ForegroundColor DarkGray }

Write-Host "========================================" -ForegroundColor Yellow
Write-Host " DashScope / AI 助手连接诊断 (Revit 外版)"
Write-Host "========================================"
Write-Info ("Endpoint : {0}" -f $Endpoint)
Write-Info ("Model    : {0}" -f $Model)

# ---------------- Step 1: DNS + TCP 443 ----------------
Write-Step 1 "网络层: DNS 解析 + TCP:443 握手"
try {
    $uri = [System.Uri]$Endpoint
    $hostName = $uri.Host
    Write-Info ("Resolving host: {0}" -f $hostName)
    $ips = [System.Net.Dns]::GetHostAddresses($hostName)
    Write-Pass ("DNS 命中 {0} 个 IP: {1}" -f $ips.Count, (($ips | Select-Object -First 3 | ForEach-Object { $_.IPAddressToString }) -join ", "))
} catch {
    Write-Fail ("DNS 失败: {0}" -f $_.Exception.InnerException.Message)
    Write-Host "      -> 建议：检查代理/VPN/防火墙，或换阿里百炼官方文档确认 Endpoint"
    return
}

try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $ias = [System.IAsyncResult]$tcp.BeginConnect($hostName, 443, $null, $null)
    $ok = $ias.AsyncWaitHandle.WaitOne(3000, $false)
    if (!$ok) { throw "TCP connect timeout (3s)" }
    $tcp.EndConnect($ias) | Out-Null
    $tcp.Close()
    Write-Pass ("TCP {0}:443 握手成功 (<3s)" -f $hostName)
} catch {
    Write-Fail ("TCP 443 失败: {0}" -f $_.Exception.Message)
    Write-Host "      -> 建议: 开个浏览器访问 https://dashscope.console.aliyun.com/ 如果能打开，说明是机器级代理阻挡 PowerShell"
    Write-Host "      -> 在 PowerShell 里跑: netsh winhttp show proxy  查看系统代理设置"
    return
}

# ---------------- Step 2: HTTP 可达性 ----------------
Write-Step 2 "传输层: HTTPS 请求 (HEAD + OPTIONS 预检)"
try {
    $resp = Invoke-WebRequest -Uri $Endpoint -Method Options -UseBasicParsing -TimeoutSec 8
    Write-Pass ("OPTIONS 响应 HTTP {0}" -f [int]$resp.StatusCode)
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    if ($code) {
        if ([int]$code -ge 400 -and [int]$code -lt 500) {
            Write-Pass ("OPTIONS 命中端点 (HTTP {0}, 4xx 表示服务可达，属于正常)" -f [int]$code)
        } else {
            Write-Fail ("OPTIONS HTTP {0}: {1}" -f [int]$code, $_.Exception.Message)
        }
    } else {
        Write-Fail ("网络层异常: " + $_.Exception.Message)
        Write-Host "      -> 如果公司用代理, 执行: [System.Net.WebRequest]::DefaultWebProxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials"
    }
}

# ---------------- Step 3: 真正模型调用 (鉴权 + 模型层) ----------------
Write-Step 3 "模型层: 发送 1 token 最小对话 (验证 Key + 模型匹配)"

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = Read-Host "请粘贴你的 DashScope API Key (sk-...) 然后回车 "
}
$ApiKey = $ApiKey.Trim()
if ([string]::IsNullOrWhiteSpace($ApiKey) -or $ApiKey.Length -lt 8) {
    Write-Fail ("API Key 为空或太短 (len={0})" -f $ApiKey.Length)
    Write-Host ""
    Write-Host "获取免费 Key: https://dashscope.console.aliyun.com/apiKey" -ForegroundColor Yellow
    return
}
if ($ApiKey -match "\.") {
    Write-Host ""
    Write-Host ("  WARN 你的 Key 含有 '.' 字符 => 这是 STS 临时签名 Token，1 小时左右就过期 !") -ForegroundColor Magenta
    Write-Host "         请去控制台创建普通 API-Key (只以 sk- 开头，没有点号)" -ForegroundColor Magenta
    Write-Host ""
}

$bodyObj = @{
    model    = $Model
    messages = @(@{ role = "user"; content = "Hi" })
    max_tokens = 3
    stream   = $false
}
$jsonBody = $bodyObj | ConvertTo-Json -Depth 5

try {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $resp = Invoke-RestMethod -Uri $Endpoint -Method Post `
        -Headers @{ "Authorization" = "Bearer $ApiKey"; "Content-Type" = "application/json" } `
        -Body $jsonBody -TimeoutSec 20
    $sw.Stop()

    $reply = $resp.choices[0].message.content
    $usage = $resp.usage
    Write-Pass ("模型调用成功! 耗时 {0} ms" -f $sw.ElapsedMilliseconds)
    Write-Info ("模型回: {0}" -f ($reply -replace "`r|`n", " "))
    if ($usage) {
        Write-Info ("tokens: prompt={0}  completion={1}  total={2}" -f $usage.prompt_tokens, $usage.completion_tokens, $usage.total_tokens)
    }
    Write-Host ""
    Write-Host "==> 结论: 网络/鉴权/模型三层全部 OK! 把相同 Key 填进 AI 助手设置即可。" -ForegroundColor Green
} catch {
    $respStream = $_.Exception.Response
    $code = 0
    try { $code = [int]$respStream.StatusCode.value__ } catch {}
    $errBody = ""
    try {
        $reader = New-Object System.IO.StreamReader($respStream.GetResponseStream())
        $errBody = $reader.ReadToEnd()
    } catch {}
    Write-Fail ("模型调用失败 HTTP {0}" -f $(if($code){$code}else{"(no response)"}))
    if ($errBody) {
        Write-Info ("原始错误体: {0}" -f $errBody)
    }

    switch ([int]$code) {
        401 {
            Write-Host ""
            Write-Host "  === 401 Unauthorized 排错清单 ===" -ForegroundColor Red
            Write-Host "  1) Key 前面有没有误粘空格? 复制过来手敲 'Bearer ' 时不要输错"
            Write-Host "  2) 是不是在控制台用错了产品? 必须是 '百炼 DashScope' 的 API Key，不是阿里云主账号 AccessKey"
            Write-Host "  3) 去控制台 https://dashscope.console.aliyun.com/apiKey 点右侧 '显示' 对比一下字符"
            Write-Host "  4) 如果 Key 包含点号，那是 STS 临时 Token，请重新创建"
        }
        404 {
            Write-Host ""
            Write-Host "  === 404 Not Found 排错清单 ===" -ForegroundColor Red
            Write-Host ("  当前模型 = '{0}'" -f $Model)
            Write-Host "  1) 兼容模式端点只支持 OpenAI 兼容模型名: qwen-turbo  qwen-plus  qwen-max  qwen-long"
            Write-Host "  2) 如果想用 DashScope 原生端点, 把 Endpoint 改为: https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation (注意 body 格式不一样)"
            Write-Host "  3) 请访问 https://help.aliyun.com/zh/model-studio/developer-reference/use-qwen-by-calling-openai-compatible-api 核对兼容列表"
        }
        429 {
            Write-Host ""
            Write-Host "  === 429 限流 ===" -ForegroundColor Yellow
            Write-Host "  新开通的账户默认 RPM/TPM 比较低，稍等 1 分钟再试即可"
            Write-Host "  控制台费用中心确认是否欠费"
        }
        default {
            Write-Host ("  原始异常 Message: {0}" -f $_.Exception.Message) -ForegroundColor Red
        }
    }
}

Write-Host ""
Read-Host "按回车键退出"
