param(
  [string]$Root = "."
)

$ErrorActionPreference = 'Stop'

# 说明：
# - [\uD83C-\uDBFF][\uDC00-\uDFFF] ：匹配任意 UTF-16 代理项对（覆盖 U+10000 以上的大多数 emoji）
# - [\u2600-\u27BF] ：补上较老的符号 emoji（如 ☀★⚙）
# - 另外把 ZWJ(200D) 与 VS-16(FE0F) 作为 emoji 组合的信号进行辅助匹配
$emojiPattern = '([\uD83C-\uDBFF][\uDC00-\uDFFF])|([\u2600-\u27BF])|(\u200D)|(\uFE0F)'

$found  = $false
$report = Join-Path (Get-Location) 'emoji_report.txt'
if (Test-Path $report) { Remove-Item $report -Force }

# 递归扫描 *.cs，排除 bin/obj/.git 目录
Get-ChildItem -Path $Root -Recurse -Filter *.cs -File |
  Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' } |
  ForEach-Object {
    $path = $_.FullName
    $printed = $false
    $lineNo = 0

    # 用逐行读取来输出“文件名 + 行号 + 行内容”
    Get-Content -LiteralPath $path -Encoding UTF8 | ForEach-Object {
      $lineNo++
      if ($_ -match $emojiPattern) {
        if (-not $printed) {
          Write-Host "包含 Emoji => $path" -ForegroundColor Red
          Add-Content -Path $report -Encoding UTF8 -Value "[FILE] $path"
          $printed = $true
          $found = $true
        }
        $out = ('{0,6}: {1}' -f $lineNo, $_)
        Write-Host $out
        Add-Content -Path $report -Encoding UTF8 -Value $out
      }
    }
  }

if ($found) {
  Write-Host "`n>> 发现存在 Emoji 的文件，详情见 emoji_report.txt" -ForegroundColor Yellow
  exit 1
} else {
  Write-Host "未发现 Emoji。"
  exit 0
}

