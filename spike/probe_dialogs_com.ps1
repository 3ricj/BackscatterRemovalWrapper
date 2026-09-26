$ErrorActionPreference = 'Continue'
$ps = New-Object -ComObject Photoshop.Application

foreach ($v in 1,2,3) {
    try {
        $ps.DisplayDialogs = $v
        Write-Output ("COM set DisplayDialogs=$v -> readback=" + $ps.DisplayDialogs)
    } catch {
        Write-Output ("COM set DisplayDialogs=$v -> ERROR: " + $_.Exception.Message)
    }
}
