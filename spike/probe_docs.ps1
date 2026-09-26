# Diagnostic probe: enumerate every document Photoshop currently has open.
# A stray open document (especially a Camera Raw / 16-bit one) is a prime suspect for
# the "the command 'make' is not available" error raised mid-action under automation.

$ErrorActionPreference = 'Stop'
$type = [Type]::GetTypeFromProgID('Photoshop.Application')
$ps = [Activator]::CreateInstance($type)

Write-Output ("Photoshop version : " + $ps.Version)
Write-Output ("DisplayDialogs    : " + $ps.DisplayDialogs)
Write-Output ("Documents open    : " + $ps.Documents.Count)
try   { Write-Output ("ActiveDocument    : " + $ps.ActiveDocument.Name) }
catch { Write-Output ("ActiveDocument    : <none / error: " + $_.Exception.Message + ">") }

Write-Output ''
$i = 0
foreach ($d in $ps.Documents) {
    Write-Output ("[{0}] name          : {1}" -f $i, $d.Name)
    try { Write-Output ("     bits/mode    : {0} / {1}"      -f $d.BitsPerChannel, $d.Mode) } catch { Write-Output ("     bits/mode    : <err>") }
    try { Write-Output ("     size         : {0} x {1}"      -f $d.Width, $d.Height) }       catch { Write-Output ("     size         : <err>") }
    try { Write-Output ("     modified     : {0}"            -f $d.Modified) }                catch { Write-Output ("     modified     : <err>") }
    try { Write-Output ("     profile      : {0}"            -f $d.ColorProfileName) }        catch { Write-Output ("     profile      : <err>") }
    try { Write-Output ("     layers       : {0}"            -f $d.Layers.Count) }            catch { Write-Output ("     layers       : <err>") }
    try { Write-Output ("     histStates   : {0}"            -f $d.HistoryStates.Count) }    catch { Write-Output ("     histStates   : <err>") }
    try {
        $names = @()
        for ($h = 0; $h -lt $d.HistoryStates.Count; $h++) { $names += ('{0}:{1}' -f $h, $d.HistoryStates.Item($h).Name) }
        Write-Output ("     history      : " + ($names -join ' | '))
    } catch { Write-Output ("     history      : <err>") }
    Write-Output ''
    $i++
}
