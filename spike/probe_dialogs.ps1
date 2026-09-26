$ErrorActionPreference = 'Stop'

$js = @'
var out = [];
try { out.push("DisplayDialogs.ALLDIALOGS=" + DisplayDialogs.ALLDIALOGS); } catch (e) { out.push("ALLDIALOGS err: " + e); }
try { out.push("DisplayDialogs.ERRORDIALOGS=" + DisplayDialogs.ERRORDIALOGS); } catch (e) { out.push("ERRORDIALOGS err: " + e); }
try { out.push("DisplayDialogs.NO_DIALOGS=" + DisplayDialogs.NO_DIALOGS); } catch (e) { out.push("NO_DIALOGS err: " + e); }
for (var v = 1; v <= 4; v++) {
  try {
    app.displayDialogs = v;
    out.push("set " + v + " -> readback=" + app.displayDialogs);
  } catch (e) {
    out.push("set " + v + " -> ERROR " + e);
  }
}
out.join("\n");
'@

$ps = New-Object -ComObject Photoshop.Application
Write-Output $ps.DoJavaScript($js)
