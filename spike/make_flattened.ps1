# Creates a FLATTENED 16-bit copy of small16.psd as flat16.psd.
#
# Hypothesis under test: the BSXT action's failing step (Make channel / At: mask channel /
# Using: reveal all = Layer > Layer Mask > Reveal All) fails because the document already
# contains the action's own layer stack ("BSXT" x4, "Cleanup" x2) from a previous run —
# i.e. the action does not tolerate being re-run on an already-processed document.
# A flattened copy has no such leftovers: if the spike succeeds on flat16.psd, the
# "already-processed layers" hypothesis is confirmed and bit depth is irrelevant.

$ErrorActionPreference = 'Stop'

$src = (Resolve-Path .\testdata\small16.psd).Path
$dst = Join-Path (Get-Location) 'testdata\flat16.psd'

$js = @'
app.activeDocument.flatten();
'@

$psType = [Type]::GetTypeFromProgID('Photoshop.Application')
$ps = [Activator]::CreateInstance($psType)
$ps.DisplayDialogs = 3   # psDisplayNoDialogs

# Close anything left open from a previous (failed) run so we start clean.
while ($ps.Documents.Count -gt 0) {
    $ps.Documents.Item(1).Close(2)   # 2 = psDoNotSaveChanges
}
Write-Output ("docs open after cleanup : " + $ps.Documents.Count)

$doc = $ps.Open($src)
$ps.DoJavaScript($js, $null, [int]3) | Out-Null
Write-Output ("layers after flatten : " + $doc.Layers.Count)

$opts = New-Object -ComObject 'Photoshop.PSDSaveOptions'
$opts.Layers = $false
$opts.EmbedColorProfile = $true
$opts.MaximizeLayers = $true

# asCopy = false is fine here: this opened copy IS the throwaway artifact we save.
$doc.SaveAs($dst, $opts, $false)
$doc.Close(2)   # psDoNotSaveChanges (COM expects an int flag)

Get-Item $dst | Select-Object Name, Length, LastWriteTime | Format-List
