param(
    [Parameter(Mandatory = $true)][string] $CandidateFile,
    [string] $RetainedFile
)
$ErrorActionPreference = 'Stop'

function Read-ReviewedRows([string] $Path) {
    $resolved = Resolve-Path -LiteralPath $Path
    if ((Get-Item -LiteralPath $resolved).Length -gt 8MB) { throw 'Dataset exceeds bounded size.' }
    $rows = @(Get-Content -LiteralPath $resolved -Raw -Encoding utf8 | ConvertFrom-Json)
    if ($rows.Count -eq 0) { throw 'Empty geography dataset.' }
    $keys = [System.Collections.Generic.HashSet[string]]::new()
    $areas = @{}
    foreach ($row in $rows) {
        $province = [string]$row.provinceCode
        $district = [string]$row.districtCode
        $subdistrict = [string]$row.subdistrictCode
        $postcode = [string]$row.postalCode
        if ($province -notmatch '^[0-9]{2}$' -or $district -notmatch '^[0-9]{4}$' -or $subdistrict -notmatch '^[0-9]{6}$' -or $postcode -notmatch '^[0-9]{5}$') { throw 'Invalid code/postcode shape.' }
        if (-not $district.StartsWith($province) -or -not $subdistrict.StartsWith($district)) { throw 'Inconsistent hierarchy.' }
        if (-not $keys.Add("${subdistrict}:$postcode")) { throw 'Duplicate postcode relationship.' }
        foreach ($level in @('province', 'district', 'subdistrict')) {
            $code = [string]$row."${level}Code"
            $thai = [string]$row."${level}NameTh"
            $english = [string]$row."${level}NameEn"
            if ([string]::IsNullOrWhiteSpace($thai) -or [string]::IsNullOrWhiteSpace($english)) { throw 'Missing bilingual name.' }
            $key = "${level}:$code"
            $value = "$thai|$english"
            if ($areas.ContainsKey($key) -and $areas[$key] -ne $value) { throw 'Conflicting names for stable code.' }
            $areas[$key] = $value
        }
    }
    return $rows
}

$candidate = @(Read-ReviewedRows $CandidateFile)
$report = [ordered]@{
    sha256 = (Get-FileHash -LiteralPath $CandidateFile -Algorithm SHA256).Hash
    combinations = $candidate.Count
    provinces = @($candidate.provinceCode | Sort-Object -Unique).Count
    districts = @($candidate.districtCode | Sort-Object -Unique).Count
    subdistricts = @($candidate.subdistrictCode | Sort-Object -Unique).Count
    retainedComparison = 'pending-reviewed-retained-export'
    added = @()
    removed = @()
    changed = @()
}
if ($RetainedFile) {
    $retained = @(Read-ReviewedRows $RetainedFile)
    $old = @{}
    $new = @{}
    foreach ($row in $retained) { $old["$($row.subdistrictCode):$($row.postalCode)"] = $row }
    foreach ($row in $candidate) { $new["$($row.subdistrictCode):$($row.postalCode)"] = $row }
    $report.added = @($new.Keys | Where-Object { -not $old.ContainsKey($_) } | Sort-Object)
    $report.removed = @($old.Keys | Where-Object { -not $new.ContainsKey($_) } | Sort-Object)
    $fields = @('provinceCode','provinceNameTh','provinceNameEn','districtCode','districtNameTh','districtNameEn','subdistrictNameTh','subdistrictNameEn')
    $report.changed = @($new.Keys | Where-Object {
        $key = $_
        $old.ContainsKey($key) -and @($fields | Where-Object { [string]$old[$key].$_ -cne [string]$new[$key].$_ }).Count -gt 0
    } | Sort-Object)
    $report.retainedComparison = 'diff-produced-review-required'
}
$report | ConvertTo-Json -Depth 5
