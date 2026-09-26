$ErrorActionPreference = 'Stop'

# Create a SMALL 16-bit TIFF from the large 16-bit sample, using Photoshop itself.
$js = @'
var src = new File("C:/Users/3ricj/Documents/GitHub/BackscatterRemovalWrapper/testdata/sample.tif");
var doc = app.open(src);
var info = "bits=" + doc.bitsPerChannel + " mode=" + doc.mode + " profile=" + doc.colorProfileName + " w=" + doc.width + " h=" + doc.height;
doc.resizeImage(1200, null, null, ResampleMethod.BICUBIC);
doc.saveAs(new File("C:/Users/3ricj/Documents/GitHub/BackscatterRemovalWrapper/testdata/small16.tif"), undefined, true);
doc.close(SaveOptions.DONOTSAVECHANGES);
info;
'@

$ps = New-Object -ComObject Photoshop.Application
Write-Output $ps.DoJavaScript($js)
