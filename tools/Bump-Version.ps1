<#
.SYNOPSIS
PensionCompass\Package.appxmanifest 의 <Identity Version="..."/> 값을 한 자리 올립니다.

.DESCRIPTION
사이드로드(.msix) 배포 시 Identity Version 이 직전 패키지와 같으면 받는 PC에서
"이미 같은 버전이 설치됨" 으로 거부됩니다. 새 빌드를 만들기 직전에 이 스크립트를 한 번
돌려서 버전을 올리는 것이 목적입니다.

기본 동작은 세 번째 자리(build) +1, 네 번째 자리(revision) 0 리셋입니다.
나중에 MS 스토어로 옮기게 되면 Microsoft 가 revision 자리를 자동으로 채우므로,
그때까진 build 자리를 쓰는 것이 안전합니다.

파일 인코딩(UTF-8 BOM)과 줄 끝(CRLF) 은 보존합니다.

.PARAMETER Major
첫 번째 자리(major) +1, 그 아래 모두 0 리셋.

.PARAMETER Minor
두 번째 자리(minor) +1, build/revision 0 리셋.

.PARAMETER Build
세 번째 자리(build) +1, revision 0 리셋. (기본값)

.PARAMETER Revision
네 번째 자리(revision) +1.

.PARAMETER DryRun
파일을 수정하지 않고 변경 결과만 출력합니다.

.EXAMPLE
.\tools\Bump-Version.ps1
# 1.0.0.0 → 1.0.1.0

.EXAMPLE
.\tools\Bump-Version.ps1 -Minor
# 1.0.5.0 → 1.1.0.0

.EXAMPLE
.\tools\Bump-Version.ps1 -DryRun
# 변경될 결과만 출력
#>
[CmdletBinding()]
param(
    [switch]$Major,
    [switch]$Minor,
    [switch]$Build,
    [switch]$Revision,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# 스크립트가 어디서 호출되든 동일하게 동작하도록 리포 루트 기준으로 경로 계산
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot    = Split-Path -Parent $ScriptDir
$ManifestPath = Join-Path $RepoRoot 'PensionCompass\Package.appxmanifest'

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Package.appxmanifest 를 찾을 수 없습니다: $ManifestPath"
}

# 위치 플래그는 정확히 하나만. 아무것도 없으면 -Build 가 기본.
$flagCount = @($Major, $Minor, $Build, $Revision | Where-Object { $_ }).Count
if ($flagCount -gt 1) {
    throw '-Major, -Minor, -Build, -Revision 중 하나만 지정하세요.'
}
if ($flagCount -eq 0) { $Build = $true }

# 파일은 byte 로 읽어 BOM 유무를 정확히 판별
$bytes  = [System.IO.File]::ReadAllBytes($ManifestPath)
$hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
$encoding = New-Object System.Text.UTF8Encoding($hasBom)
$startIdx = if ($hasBom) { 3 } else { 0 }
$content  = $encoding.GetString($bytes, $startIdx, $bytes.Length - $startIdx)

# Identity 요소 안의 Version 속성만 잡도록 한정 (PhoneIdentity 등 다른 요소 영향 X)
$pattern = '(<Identity\b[^>]*\bVersion=")(\d+)\.(\d+)\.(\d+)\.(\d+)(")'
$match   = [regex]::Match($content, $pattern)
if (-not $match.Success) {
    throw 'Identity 요소의 Version 속성을 찾지 못했습니다.'
}

$old = "$($match.Groups[2].Value).$($match.Groups[3].Value).$($match.Groups[4].Value).$($match.Groups[5].Value)"
$ma  = [int]$match.Groups[2].Value
$mi  = [int]$match.Groups[3].Value
$bd  = [int]$match.Groups[4].Value
$rv  = [int]$match.Groups[5].Value

if     ($Major)    { $ma++; $mi = 0; $bd = 0; $rv = 0 }
elseif ($Minor)    { $mi++; $bd = 0; $rv = 0 }
elseif ($Build)    { $bd++; $rv = 0 }
elseif ($Revision) { $rv++ }

$new        = "$ma.$mi.$bd.$rv"
$replacement = "$($match.Groups[1].Value)$new$($match.Groups[6].Value)"
$updated    = [regex]::Replace($content, $pattern, $replacement)

Write-Host "이전: $old"
Write-Host "이후: $new"

if ($DryRun) {
    Write-Host '[DryRun] 파일을 수정하지 않습니다.'
    return
}

# 원본과 동일한 인코딩(UTF-8 BOM 유/무)으로 다시 저장. CRLF 는 문자열 안에 그대로 보존됨.
[System.IO.File]::WriteAllText($ManifestPath, $updated, $encoding)
Write-Host 'Package.appxmanifest 업데이트 완료.'
