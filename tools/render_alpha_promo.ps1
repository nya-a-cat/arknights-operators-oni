param(
	[string]$ScreenshotRoot = "C:\Program Files (x86)\Steam\userdata\1018078487\760\remote\457140\screenshots",
	[string]$OutputPath = ""
)

Add-Type -AssemblyName System.Drawing

function ConvertFrom-CodePoints([int[]]$Codes) {
	return -join ($Codes | ForEach-Object { [char]$_ })
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
	$OutputPath = Join-Path $repoRoot "docs\images\arknights-oni-alpha-v0.3.2-workshop.png"
}

$exusiaiCn = ConvertFrom-CodePoints @(0x80FD, 0x5929, 0x4F7F)
$surtrCn = ConvertFrom-CodePoints @(0x53F2, 0x5C14, 0x7279, 0x5C14)
$amiyaCn = ConvertFrom-CodePoints @(0x963F, 0x7C73, 0x5A05)
$texasCn = ConvertFrom-CodePoints @(0x5FB7, 0x514B, 0x8428, 0x65AF)
$arknightsCn = ConvertFrom-CodePoints @(0x660E, 0x65E5, 0x65B9, 0x821F, 0x5E72, 0x5458)
$oniCn = ConvertFrom-CodePoints @(0x7F3A, 0x6C27)
$cards = @(
	@{ File = "20260715125124_1.jpg"; Label = "EXUSIAI / $exusiaiCn" },
	@{ File = "20260715140728_1.jpg"; Label = "SURTR / $surtrCn" },
	@{ File = "20260715140920_1.jpg"; Label = "AMIYA / $amiyaCn" },
	@{ File = "20260715141342_1.jpg"; Label = "TEXAS / $texasCn" }
)

$outputDirectory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$canvas = New-Object System.Drawing.Bitmap 1920, 1080
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

$background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 15, 19, 27))
$header = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 25, 31, 43))
$accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 129, 46))
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 187, 198, 214))
$footer = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(215, 11, 15, 22))
$border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 64, 76, 94)), 4
$accentPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 240, 129, 46)), 6
$titleFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 42, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$subtitleFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 28, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$cardFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 25, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$badgeFont = New-Object System.Drawing.Font "Microsoft YaHei UI", 25, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)

try {
	$graphics.FillRectangle($background, 0, 0, 1920, 1080)
	$graphics.FillRectangle($header, 0, 0, 1920, 205)
	$graphics.FillRectangle($accent, 0, 0, 18, 205)
	$graphics.DrawString("ARKNIGHTS OPERATORS X OXYGEN NOT INCLUDED", $titleFont, $white, 58, 38)
	$graphics.DrawString("$arknightsCn X $oniCn   /   449 OPERATORS   /   LIVE SWITCHING   /   CN / EN / JP", $subtitleFont, $muted, 62, 112)
	$graphics.FillRectangle($accent, 1585, 54, 270, 78)
	$graphics.DrawString("ALPHA v0.3.2", $badgeFont, $background, 1610, 76)

	for ($index = 0; $index -lt $cards.Count; $index++) {
		$card = $cards[$index]
		$sourcePath = Join-Path $ScreenshotRoot $card.File
		if (-not (Test-Path -LiteralPath $sourcePath)) {
			throw "Missing source screenshot: $sourcePath"
		}

		$image = [System.Drawing.Image]::FromFile($sourcePath)
		try {
			$x = 55 + ($index * 457)
			$destination = New-Object System.Drawing.Rectangle $x, 245, 430, 750
			$source = New-Object System.Drawing.Rectangle 660, 250, 480, 800
			$graphics.DrawImage($image, $destination, $source, [System.Drawing.GraphicsUnit]::Pixel)
			$graphics.DrawRectangle($border, $destination)
			$graphics.DrawLine($accentPen, $x, 245, ($x + 430), 245)
			$graphics.FillRectangle($footer, $x, 925, 430, 70)
			$graphics.DrawString($card.Label, $cardFont, $white, ($x + 20), 943)
		} finally {
			$image.Dispose()
		}
	}

	$graphics.DrawString("Real in-game screenshots / On-demand or permanent cache / Ctrl+F8", $subtitleFont, $muted, 58, 1021)
	$canvas.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
	$graphics.Dispose()
	$canvas.Dispose()
	$background.Dispose()
	$header.Dispose()
	$accent.Dispose()
	$white.Dispose()
	$muted.Dispose()
	$footer.Dispose()
	$border.Dispose()
	$accentPen.Dispose()
	$titleFont.Dispose()
	$subtitleFont.Dispose()
	$cardFont.Dispose()
	$badgeFont.Dispose()
}

Write-Output $OutputPath
