Add-Type -AssemblyName System.Drawing

$srcPath = "C:\Users\shuns\.gemini\antigravity-ide\brain\75d4ed24-4a40-4045-8420-214ffc22b405\.user_uploaded\media_1791188669477.jpg"
$destImgPath = "images\icon.png"
$destRootPath = "icon.png"

$src = [System.Drawing.Image]::FromFile($srcPath)
$bmp = New-Object System.Drawing.Bitmap(512, 512)
$graphics = [System.Drawing.Graphics]::FromImage($bmp)

$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

$graphics.DrawImage($src, 0, 0, 512, 512)

$bmp.Save($destImgPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Save($destRootPath, [System.Drawing.Imaging.ImageFormat]::Png)

$graphics.Dispose()
$bmp.Dispose()
$src.Dispose()

Write-Host "Icon converted successfully to 512x512 PNG at $destImgPath and $destRootPath!"
