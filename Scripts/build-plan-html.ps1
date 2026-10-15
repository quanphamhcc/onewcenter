# Builds a self-contained HTML file from Scripts\plan-content.json.
# Opens in any browser; also pastes cleanly into Google Docs.
# ASCII-only script; Vietnamese text lives in the UTF-8 JSON data file.
param(
    [string]$DataPath = (Join-Path $PSScriptRoot 'plan-content.json'),
    [string]$OutPath  = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\OWC-Presenter-Ke-Hoach-Trien-Khai.html')
)

$ErrorActionPreference = 'Stop'
$jsonText = [System.IO.File]::ReadAllText($DataPath, [System.Text.Encoding]::UTF8)
$data = $jsonText | ConvertFrom-Json

function HtmlEsc([string]$s) {
    if ($null -eq $s) { return '' }
    $s = $s -replace '&', '&amp;'
    $s = $s -replace '<', '&lt;'
    $s = $s -replace '>', '&gt;'
    return $s
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('<h1 class="title">' + (HtmlEsc $data.title) + '</h1>')
[void]$sb.Append('<p class="subtitle">' + (HtmlEsc $data.subtitle) + '</p>')

$inList = $false
function Close-List {
    if ($script:inList) { [void]$script:sb.Append('</ul>'); $script:inList = $false }
}

foreach ($blk in $data.blocks) {
    switch ($blk.type) {
        'h1' { Close-List; [void]$sb.Append('<h2>' + (HtmlEsc $blk.text) + '</h2>') }
        'p'  { Close-List; [void]$sb.Append('<p>' + (HtmlEsc $blk.text) + '</p>') }
        'p-bold' { Close-List; [void]$sb.Append('<p class="note"><strong>' + (HtmlEsc $blk.text) + '</strong></p>') }
        'bullet' {
            if (-not $script:inList) { [void]$sb.Append('<ul>'); $script:inList = $true }
            [void]$sb.Append('<li>' + (HtmlEsc $blk.text) + '</li>')
        }
        'table' {
            Close-List
            [void]$sb.Append('<table>')
            [void]$sb.Append('<thead><tr>')
            foreach ($h in $blk.headers) { [void]$sb.Append('<th>' + (HtmlEsc $h) + '</th>') }
            [void]$sb.Append('</tr></thead><tbody>')
            foreach ($row in $blk.rows) {
                [void]$sb.Append('<tr>')
                foreach ($cell in $row) { [void]$sb.Append('<td>' + (HtmlEsc $cell) + '</td>') }
                [void]$sb.Append('</tr>')
            }
            [void]$sb.Append('</tbody></table>')
        }
    }
}
Close-List

$css = @'
body{font-family:Segoe UI,Arial,sans-serif;max-width:900px;margin:30px auto;padding:0 24px;color:#1b2a2f;line-height:1.5}
h1.title{color:#2d6a4f;font-size:26px;margin-bottom:4px}
p.subtitle{color:#52796f;font-style:italic;font-size:15px;margin-top:0}
h2{color:#264653;font-size:19px;margin-top:28px;border-bottom:2px solid #e9ecef;padding-bottom:4px}
table{border-collapse:collapse;width:100%;margin:12px 0;font-size:14px}
th{background:#264653;color:#fff;text-align:left;padding:8px 10px;border:1px solid #bfc7cc}
td{padding:7px 10px;border:1px solid #bfc7cc;vertical-align:top}
tbody tr:nth-child(even){background:#f2f4f5}
ul{margin:8px 0}
li{margin:4px 0}
p.note{background:#fff6e6;border-left:4px solid #f4a261;padding:8px 12px;margin:12px 0}
'@

$html = '<!DOCTYPE html><html lang="vi"><head><meta charset="UTF-8">' +
        '<meta name="viewport" content="width=device-width, initial-scale=1">' +
        '<title>' + (HtmlEsc $data.title) + '</title><style>' + $css + '</style></head><body>' +
        $sb.ToString() + '</body></html>'

$outDir = Split-Path $OutPath -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$utf8 = New-Object System.Text.UTF8Encoding $true   # with BOM for safe browser detection
[System.IO.File]::WriteAllText($OutPath, $html, $utf8)

Write-Output "HTML created: $OutPath"
Write-Output ("Size: {0} KB" -f [math]::Round((Get-Item $OutPath).Length/1KB,1))
