# Diagnostic: reproduce the action's failing recorded step in isolation.
#
# The failing step per the Actions panel is:
#     Make  channel
#         At:   mask channel
#         Using: reveal all
# i.e. Layer > Layer Mask > Reveal All.
#
# Photoshop reports "The command 'Make' is not available" when a mask cannot be created
# on the target layer - classically because the layer ALREADY has a layer mask.
#
# This script opens a document, dumps the real layer/mask state (index-based, because
# these documents contain several layers with the SAME name), then attempts the exact
# 'make mask' ActionManager call on the active layer with dialogs OFF so the genuine
# Photoshop error string is returned instead of "User cancelled". Nothing is saved.
#
# Usage: powershell -File spike\probe_masks.ps1 -Path .\testdata\small16.psd

param(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'
$full = (Resolve-Path $Path).Path

$js = @'
var __out = [];
app.displayDialogs = DialogModes.NO;
var d = app.activeDocument;
__out.push('doc      = ' + d.name + ' | ' + d.bitsPerChannel + ' | ' + d.width + 'x' + d.height);

function maskByIndex(idx) {
    var r = new ActionReference();
    r.putProperty(charIDToTypeID('Chnl'), charIDToTypeID('Msk '));
    r.putIndex(charIDToTypeID('Lyr '), idx);
    try { executeActionGet(r); return 'HAS_MASK'; } catch (e) { return 'none'; }
}

try {
    var al = d.activeLayer;
    __out.push('active   = "' + al.name + '" (' + al.typename + ') index=' + al.index +
               ' mask=' + maskByIndex(al.index));
} catch (e) { __out.push('active ERR ' + e); }

function walk(layers, depth) {
    for (var i = 0; i < layers.length; i++) {
        var L = layers[i];
        var pad = '';
        for (var p = 0; p < depth; p++) pad += '  ';
        var idx = '?', msk = '?';
        try { idx = L.index; } catch (e1) {}
        try { msk = maskByIndex(L.index); } catch (e2) {}
        __out.push(pad + '[' + idx + '] ' + L.typename + ' "' + L.name + '" mask=' + msk +
                   ' opaque=' + L.opacity);
        if (L.typename === 'LayerSet') walk(L.layers, depth + 1);
    }
}
try { walk(d.layers, 0); } catch (e) { __out.push('walk ERR ' + e); }

// Attempt the failing step verbatim on the current active layer.
try {
    var desc = new ActionDescriptor();
    var ref = new ActionReference();
    ref.putProperty(stringIDToTypeID('channel'), stringIDToTypeID('mask'));
    ref.putEnumerated(stringIDToTypeID('channel'),
                      stringIDToTypeID('channel'),
                      stringIDToTypeID('mask'));
    desc.putReference(stringIDToTypeID('at'), ref);
    desc.putEnumerated(stringIDToTypeID('using'),
                       stringIDToTypeID('mask'),
                       stringIDToTypeID('revealAll'));
    executeAction(stringIDToTypeID('make'), desc, DialogModes.NO);
    __out.push('MAKE_MASK  => OK');
} catch (eMake) {
    __out.push('MAKE_MASK  => ERROR: ' + eMake);
}

__out.join('\n');
'@

$psType = [Type]::GetTypeFromProgID('Photoshop.Application')
$ps = [Activator]::CreateInstance($psType)

$ps.Open($full) | Out-Null
$result = $ps.DoJavaScript($js, $null, [int]3)   # 3 = psDisplayNoDialogs (verified on PS 27)
Write-Output ("---- " + [IO.Path]::GetFileName($full) + " ----")
$result -split "`r?`n" | ForEach-Object { Write-Output $_ }

# Discard: never save the probe's modifications. (COM expects an int flag.)
$ps.ActiveDocument.Close(2)   # 2 = psDoNotSaveChanges
