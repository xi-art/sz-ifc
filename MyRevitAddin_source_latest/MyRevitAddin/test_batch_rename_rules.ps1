# ============================================================
#  Batch Rename Rule Self-Test
#  (mirrors BatchRenameFamiliesDialog.ApplyAllRules order)
#  No Chinese to avoid PS default-codepage parse issues
# ============================================================
$ErrorActionPreference = 'Stop'

function ReplaceCaseSensitive($s, $find, $replace, [StringComparison]$sc) {
    if ([string]::IsNullOrEmpty($find)) { return $s }
    $idx = 0
    while (($idx = $s.IndexOf($find, $idx, $sc)) -ge 0) {
        $s = $s.Remove($idx, $find.Length).Insert($idx, $replace)
        $idx += $replace.Length
        if ($idx -gt $s.Length) { break }
    }
    return $s
}

function ApplyAllRules {
    param(
        [string]$original,
        [int]$counterIndex = -1,
        [string]$categoryName = "",
        [hashtable]$cfg
    )
    $s = if ($original -eq $null) { "" } else { $original }

    $ts = [int]$cfg.numTrimStart; $te = [int]$cfg.numTrimEnd
    if ($ts -gt 0) { $s = if ($ts -lt $s.Length) { $s.Substring($ts) } else { "" } }
    if ($te -gt 0) { $s = if ($te -lt $s.Length) { $s.Substring(0, $s.Length - $te) } else { "" } }

    $pos = [int]$cfg.numInsertPos
    $insertT = if ($cfg.txtInsert -eq $null) { "" } else { $cfg.txtInsert }
    if ($insertT.Length -gt 0) {
        $pos = [Math]::Min([Math]::Max(0, $pos), $s.Length)
        $s = $s.Insert($pos, $insertT)
    }

    if ([bool]$cfg.useCategoryPrefix -and -not [string]::IsNullOrEmpty($categoryName)) {
        $prefix = $categoryName.Trim() + "_"
        if (-not $s.StartsWith($prefix, [StringComparison]::Ordinal)) {
            $s = $prefix + $s
        }
    }

    $f = if ($cfg.txtFind -eq $null) { "" } else { $cfg.txtFind }
    $r = if ($cfg.txtReplace -eq $null) { "" } else { $cfg.txtReplace }
    if ($f.Length -gt 0) {
        try {
            if ([bool]$cfg.useRegex) {
                $opt = if ([bool]$cfg.ignoreCase) { [System.Text.RegularExpressions.RegexOptions]::IgnoreCase } else { [System.Text.RegularExpressions.RegexOptions]::None }
                $s = [regex]::Replace($s, $f, $r, $opt)
            } else {
                $sc = if ([bool]$cfg.ignoreCase) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
                $s = ReplaceCaseSensitive $s $f $r $sc
            }
        } catch { <# ignore regex errors #> }
    }

    $pre = if ($cfg.txtPrefix -eq $null) { "" } else { $cfg.txtPrefix }
    $suf = if ($cfg.txtSuffix -eq $null) { "" } else { $cfg.txtSuffix }
    if ($pre.Length -gt 0 -and -not $s.StartsWith($pre, [StringComparison]::Ordinal)) { $s = $pre + $s }
    if ($suf.Length -gt 0 -and -not $s.EndsWith($suf, [StringComparison]::Ordinal)) { $s = $s + $suf }

    $nd = [int]$cfg.counterDigits
    if ($nd -gt 0 -and $counterIndex -ge 0) {
        $sep = if ($cfg.counterSep -eq $null) { "_" } else { $cfg.counterSep }
        $start = [int]$cfg.counterStart
        $num = ($start + $counterIndex).ToString().PadLeft($nd, '0')
        $s = $s + $sep + $num
    }

    return $s
}

function EmptyCfg {
    @{
        txtPrefix = ""; txtSuffix = ""; useCategoryPrefix = $false
        txtFind = ""; txtReplace = ""; ignoreCase = $false; useRegex = $false
        numInsertPos = 0; txtInsert = ""
        numTrimStart = 0; numTrimEnd = 0
        counterSep = "_"; counterStart = 1; counterDigits = 0
    }
}

$results = @()

# Test1 Preset1: prefix + underscore->dash
$c = EmptyCfg
$c.txtPrefix = "Bld-"; $c.txtFind = "_"; $c.txtReplace = "-"
$in = "Win_Dbl_1500x1800"; $want = "Bld-Win-Dbl-1500x1800"
$got = ApplyAllRules $in -1 "Win" $c
$results += [pscustomobject]@{Test="Preset1(prefix+udash->dash)"; In=$in; Want=$want; Got=$got; OK=($got -ceq $want)}

# Test2 Preset2: prefix + suffix
$c = EmptyCfg
$c.txtPrefix = "Str-"; $c.txtSuffix = "-A"
$in = "KL-300x600"; $want = "Str-KL-300x600-A"
$got = ApplyAllRules $in -1 "Beam" $c
$results += [pscustomobject]@{Test="Preset2(prefix+suffix)"; In=$in; Want=$want; Got=$got; OK=($got -ceq $want)}

# Test3 Preset3: prefix + regex remove all whitespace
$c = EmptyCfg
$c.txtPrefix = "Mep-"; $c.txtFind = "\s"; $c.txtReplace = ""; $c.useRegex = $true
$in  = "FCU  Ceiling  FP-68"
$want = "Mep-FCUCeilingFP-68"
$got = ApplyAllRules $in -1 "Equip" $c
$results += [pscustomobject]@{Test="Preset3(prefix+trimAllSpaces-regex)"; In=$in; Want=$want; Got=$got; OK=($got -ceq $want)}

# Test4 Preset4: category prefix + 3-digit counter per category, start 1
$c = EmptyCfg
$c.useCategoryPrefix = $true; $c.counterDigits = 3; $c.counterStart = 1; $c.counterSep = "_"
$cases = @(
    @{i=0; cat="Door"; name="D0921";     want="Door_D0921_001"},
    @{i=1; cat="Door"; name="D1021";     want="Door_D1021_002"},
    @{i=0; cat="Win";  name="W1518a";    want="Win_W1518a_001"},
    @{i=2; cat="Door"; name="D1524";     want="Door_D1524_003"}
)
foreach ($x in $cases) {
    $got = ApplyAllRules $x.name $x.i $x.cat $c
    $ok = ($got -ceq $x.want)
    $results += [pscustomobject]@{Test="Preset4(catPrefix+3Digits[$($x.cat)/i=$($x.i)])"; In=$x.name; Want=$x.want; Got=$got; OK=$ok}
}

# Test5: trim 1 leading char, then insert dash at position 3 (operates on trimmed result)
$c = EmptyCfg
$c.numTrimStart = 1; $c.numInsertPos = 3; $c.txtInsert = "-"
$in  = "XABC123"  # trim1 -> ABC123, insert3 -> ABC-123
$want = "ABC-123"
$got = ApplyAllRules $in -1 "Misc" $c
$results += [pscustomobject]@{Test="TrimStart+InsertAt3"; In=$in; Want=$want; Got=$got; OK=($got -ceq $want)}

# Test6: regex remove everything between ( ) including the parentheses
$c = EmptyCfg
$c.txtFind = "[(].*?[)]"; $c.txtReplace = ""; $c.useRegex = $true
$in  = "Door D0921 (Fire-A)"
$want = "Door D0921 "
$got = ApplyAllRules $in -1 "Door" $c
$results += [pscustomobject]@{Test="RegexStripParen"; In=$in; Want=$want; Got=$got; OK=($got -ceq $want)}

# Test7: counter disabled when counterDigits=0 or counterIndex=-1
$c = EmptyCfg
$c.counterDigits = 3; $c.counterSep = "_"
$in  = "NameOnly"; $want = "NameOnly"
$got = ApplyAllRules $in -1 "X" $c
$results += [pscustomobject]@{Test="CounterSkipped-Index-1"; In=$in; Want=$want; Got=$got; OK=($got -ceq $want)}

# Test8: prefix not doubled when already present
$c = EmptyCfg
$c.txtPrefix = "ABC-"
$in  = "ABC-Exists"; $want = "ABC-Exists"
$got = ApplyAllRules $in -1 "X" $c
$results += [pscustomobject]@{Test="PrefixNotDoubled"; In=$in; Want=$want; Got=$got; OK=($got -ceq $want)}

Write-Host ""
Write-Host "========= Batch Rename Rule Self-Test =========" -ForegroundColor Cyan
$failed = @($results | Where-Object { -not $_.OK })
if ($failed.Count -eq 0) { $countColor = 'Green' } else { $countColor = 'Red' }
Write-Host ("Total {0} | Pass {1} | Fail {2}" -f $results.Count, ($results.Count-$failed.Count), $failed.Count) -ForegroundColor $countColor
Write-Host ""
$results | Format-Table -AutoSize -Wrap Test, In, Want, Got, OK
if ($failed.Count -gt 0) {
    Write-Host "!!! FAIL DETAILS:" -ForegroundColor Red
    $failed | Format-List
    exit 1
}
Write-Host "ALL TESTS PASSED" -ForegroundColor Green
exit 0
