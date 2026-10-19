$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$destination=Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LinkMic.Windows\assets'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
function New-RoundedPath([single]$x,[single]$y,[single]$width,[single]$height,[single]$radius){
    $path=[Drawing.Drawing2D.GraphicsPath]::new();$d=$radius*2
    $path.AddArc($x,$y,$d,$d,180,90);$path.AddArc(($x+$width-$d),$y,$d,$d,270,90)
    $path.AddArc(($x+$width-$d),($y+$height-$d),$d,$d,0,90);$path.AddArc($x,($y+$height-$d),$d,$d,90,90)
    $path.CloseFigure();return $path
}
$images=@()
foreach($size in @(16,24,32,48,64,128,256)){
    $bitmap=[Drawing.Bitmap]::new($size,$size)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent);$graphics.ScaleTransform(($size/64.0),($size/64.0))
    $background=New-RoundedPath 3 3 58 58 14
    $gradient=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(3,3),[Drawing.Point]::new(61,61),[Drawing.ColorTranslator]::FromHtml('#1396A0'),[Drawing.ColorTranslator]::FromHtml('#086372'))
    $graphics.FillPath($gradient,$background)
    $microphone=New-RoundedPath 26 14 12 26 6
    $graphics.FillPath([Drawing.Brushes]::White,$microphone)
    $pen=[Drawing.Pen]::new([Drawing.Color]::White,3)
    $pen.StartCap=$pen.EndCap=[Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($pen,20,22,24,24,0,180)
    $graphics.DrawLine($pen,20,31,20,34);$graphics.DrawLine($pen,44,31,44,34)
    $graphics.DrawLine($pen,32,46,32,52);$graphics.DrawLine($pen,25,52,39,52)
    $memory=[IO.MemoryStream]::new();$bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $images+= [pscustomobject]@{size=$size;bytes=$memory.ToArray()}
    $memory.Dispose();$pen.Dispose();$microphone.Dispose();$gradient.Dispose();$background.Dispose();$graphics.Dispose();$bitmap.Dispose()
}
[IO.File]::WriteAllBytes((Join-Path $destination 'LinkMic.png'),($images | Where-Object {$_.size -eq 128}).bytes)
$file=[IO.File]::Create((Join-Path $destination 'LinkMic.ico'));$writer=[IO.BinaryWriter]::new($file)
try{
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
    $offset=6+16*$images.Count
    foreach($item in $images){
        $dimension=if($item.size -eq 256){0}else{$item.size}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$item.bytes.Length);$writer.Write([uint32]$offset)
        $offset+=$item.bytes.Length
    }
    foreach($item in $images){$writer.Write([byte[]]$item.bytes)}
}finally{$writer.Dispose();$file.Dispose()}
Get-Item -LiteralPath (Join-Path $destination 'LinkMic.ico') | Select-Object Name,Length
