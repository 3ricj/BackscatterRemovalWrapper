$ErrorActionPreference = 'Stop'

$js = @'
var ref = new ActionReference();
ref.putProperty(charIDToTypeID("Prpr"), stringIDToTypeID("action"));
ref.putEnumerated(charIDToTypeID("capp"), charIDToTypeID("Ordn"), charIDToTypeID("Trgt"));
var desc = executeActionGet(ref);
var list = desc.getList(stringIDToTypeID("action"));
var out = [];
for (var i = 0; i < list.count; i++) {
  var d = list.getObjectValue(i);
  var sn = d.hasKey(stringIDToTypeID("actionSet")) ? d.getString(stringIDToTypeID("actionSet")) : "?";
  var an = d.hasKey(stringIDToTypeID("name")) ? d.getString(stringIDToTypeID("name")) : "?";
  out.push(sn + " :: " + an);
}
out.join("\n");
'@

$ps = New-Object -ComObject Photoshop.Application
try {
    $result = $ps.DoJavaScript($js)
    Write-Output "OK"
    Write-Output $result
} catch {
    Write-Output ("FAILED: " + $_.Exception.Message)
}
