# Builds a Word .docx (Office Open XML) from Scripts\plan-content.json.
# ASCII-only script; all Vietnamese text lives in the UTF-8 JSON data file.
# No external tools needed: a .docx is a ZIP of XML parts.
param(
    [string]$DataPath = (Join-Path $PSScriptRoot 'plan-content.json'),
    [string]$OutPath  = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\OWC-Presenter-Ke-Hoach-Trien-Khai.docx')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Read the JSON data as UTF-8 explicitly (PS 5.1 default is ANSI).
$jsonText = [System.IO.File]::ReadAllText($DataPath, [System.Text.Encoding]::UTF8)
$data = $jsonText | ConvertFrom-Json

function XmlEsc([string]$s) {
    if ($null -eq $s) { return '' }
    $s = $s -replace '&', '&amp;'
    $s = $s -replace '<', '&lt;'
    $s = $s -replace '>', '&gt;'
    return $s
}

$body = New-Object System.Text.StringBuilder

function Add-Para([string]$text, [string]$style, [bool]$bold) {
    $t = XmlEsc $text
    $pPr = if ($style) { "<w:pStyle w:val=`"$style`"/>" } else { '' }
    $rPr = if ($bold) { '<w:rPr><w:b/></w:rPr>' } else { '' }
    [void]$body.Append("<w:p><w:pPr>$pPr</w:pPr><w:r>$rPr<w:t xml:space=`"preserve`">$t</w:t></w:r></w:p>")
}

function Add-Bullet([string]$text) {
    $t = XmlEsc $text
    [void]$body.Append("<w:p><w:pPr><w:pStyle w:val=`"ListParagraph`"/><w:numPr><w:ilvl w:val=`"0`"/><w:numId w:val=`"1`"/></w:numPr></w:pPr><w:r><w:t xml:space=`"preserve`">$t</w:t></w:r></w:p>")
}

function Add-Table($headers, $rows) {
    $ncol = $headers.Count
    $tblW = 9360
    $colW = [int]($tblW / $ncol)
    [void]$body.Append('<w:tbl><w:tblPr><w:tblStyle w:val="TableGrid"/><w:tblW w:w="' + $tblW + '" w:type="dxa"/><w:tblLook w:val="04A0"/></w:tblPr>')
    [void]$body.Append('<w:tblGrid>')
    for ($i = 0; $i -lt $ncol; $i++) { [void]$body.Append("<w:gridCol w:w=`"$colW`"/>") }
    [void]$body.Append('</w:tblGrid>')
    [void]$body.Append('<w:tr>')
    foreach ($h in $headers) {
        $t = XmlEsc $h
        [void]$body.Append("<w:tc><w:tcPr><w:tcW w:w=`"$colW`" w:type=`"dxa`"/><w:shd w:val=`"clear`" w:color=`"auto`" w:fill=`"264653`"/></w:tcPr><w:p><w:pPr><w:spacing w:after=`"0`"/></w:pPr><w:r><w:rPr><w:b/><w:color w:val=`"FFFFFF`"/></w:rPr><w:t xml:space=`"preserve`">$t</w:t></w:r></w:p></w:tc>")
    }
    [void]$body.Append('</w:tr>')
    $alt = $false
    foreach ($row in $rows) {
        $fill = if ($alt) { 'F2F4F5' } else { 'FFFFFF' }
        $alt = -not $alt
        [void]$body.Append('<w:tr>')
        foreach ($cell in $row) {
            $t = XmlEsc $cell
            [void]$body.Append("<w:tc><w:tcPr><w:tcW w:w=`"$colW`" w:type=`"dxa`"/><w:shd w:val=`"clear`" w:color=`"auto`" w:fill=`"$fill`"/></w:tcPr><w:p><w:pPr><w:spacing w:after=`"0`"/></w:pPr><w:r><w:t xml:space=`"preserve`">$t</w:t></w:r></w:p></w:tc>")
        }
        [void]$body.Append('</w:tr>')
    }
    [void]$body.Append('</w:tbl>')
    Add-Para '' 'Normal' $false
}

# ---- Emit content from JSON ---------------------------------------------
Add-Para $data.title 'Title' $false
Add-Para $data.subtitle 'Subtitle' $false
Add-Para '' 'Normal' $false

foreach ($blk in $data.blocks) {
    switch ($blk.type) {
        'h1'     { Add-Para $blk.text 'Heading1' $false }
        'p'      { Add-Para $blk.text 'Normal' $false }
        'p-bold' { Add-Para $blk.text 'Normal' $true }
        'bullet' { Add-Bullet $blk.text }
        'table'  { Add-Table $blk.headers $blk.rows }
    }
}

$bodyXml = $body.ToString()

$documentXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>' +
$bodyXml +
'<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>' +
'</w:body></w:document>'

$stylesXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
'<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>' +
'<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:pPr><w:spacing w:after="120"/></w:pPr></w:style>' +
'<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:pPr><w:spacing w:after="120"/></w:pPr><w:rPr><w:b/><w:color w:val="2D6A4F"/><w:sz w:val="40"/></w:rPr></w:style>' +
'<w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:pPr><w:spacing w:after="240"/></w:pPr><w:rPr><w:i/><w:color w:val="52796F"/><w:sz w:val="24"/></w:rPr></w:style>' +
'<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:before="240" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="264653"/><w:sz w:val="30"/></w:rPr></w:style>' +
'<w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="720"/></w:pPr></w:style>' +
'<w:style w:type="table" w:styleId="TableGrid"><w:name w:val="Table Grid"/><w:tblPr><w:tblBorders><w:top w:val="single" w:sz="4" w:space="0" w:color="BFC7CC"/><w:left w:val="single" w:sz="4" w:space="0" w:color="BFC7CC"/><w:bottom w:val="single" w:sz="4" w:space="0" w:color="BFC7CC"/><w:right w:val="single" w:sz="4" w:space="0" w:color="BFC7CC"/><w:insideH w:val="single" w:sz="4" w:space="0" w:color="BFC7CC"/><w:insideV w:val="single" w:sz="4" w:space="0" w:color="BFC7CC"/></w:tblBorders></w:tblPr></w:style>' +
'</w:styles>'

$numberingXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
'<w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#8226;"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum>' +
'<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num></w:numbering>'

$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
'<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
'<Default Extension="xml" ContentType="application/xml"/>' +
'<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>' +
'<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>' +
'<Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>' +
'</Types>'

$rootRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
'<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>' +
'</Relationships>'

$docRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
'<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>' +
'<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>' +
'</Relationships>'

$outDir = Split-Path $OutPath -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
if (Test-Path $OutPath) { Remove-Item $OutPath -Force }

$utf8 = New-Object System.Text.UTF8Encoding $false
$zip = [System.IO.Compression.ZipFile]::Open($OutPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    function Add-Entry($name, $content) {
        $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
        $sw = New-Object System.IO.StreamWriter($entry.Open(), $utf8)
        $sw.Write($content); $sw.Flush(); $sw.Dispose()
    }
    Add-Entry '[Content_Types].xml' $contentTypes
    Add-Entry '_rels/.rels' $rootRels
    Add-Entry 'word/document.xml' $documentXml
    Add-Entry 'word/styles.xml' $stylesXml
    Add-Entry 'word/numbering.xml' $numberingXml
    Add-Entry 'word/_rels/document.xml.rels' $docRels
} finally {
    $zip.Dispose()
}

Write-Output "DOCX created: $OutPath"
Write-Output ("Size: {0} KB" -f [math]::Round((Get-Item $OutPath).Length/1KB,1))
