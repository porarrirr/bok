param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeIconMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyIcon(IntPtr handle);
}
"@

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2

    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    return $path
}

function Get-Color {
    param(
        [string]$Hex,
        [int]$Alpha = 255
    )

    $normalized = $Hex.TrimStart('#')
    return [System.Drawing.Color]::FromArgb(
        $Alpha,
        [Convert]::ToInt32($normalized.Substring(0, 2), 16),
        [Convert]::ToInt32($normalized.Substring(2, 2), 16),
        [Convert]::ToInt32($normalized.Substring(4, 2), 16)
    )
}

function Draw-AppIcon {
    param(
        [int]$Size,
        [string]$OutputPath
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $canvas = [float]$Size
        $margin = $canvas * 0.08
        $outerRadius = $canvas * 0.24

        $backgroundPath = New-RoundedRectanglePath -X $margin -Y $margin -Width ($canvas - ($margin * 2)) -Height ($canvas - ($margin * 2)) -Radius $outerRadius
        try {
            $backgroundBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                ([System.Drawing.PointF]::new(0, 0)),
                ([System.Drawing.PointF]::new($canvas, $canvas)),
                (Get-Color '#163A5C'),
                (Get-Color '#0E1725')
            )
            try {
                $blend = New-Object System.Drawing.Drawing2D.ColorBlend
                $blend.Colors = @(
                    (Get-Color '#224B78'),
                    (Get-Color '#183B62'),
                    (Get-Color '#0E1725')
                )
                $blend.Positions = @(0.0, 0.45, 1.0)
                $backgroundBrush.InterpolationColors = $blend
                $graphics.FillPath($backgroundBrush, $backgroundPath)
            }
            finally {
                $backgroundBrush.Dispose()
            }

            $highlightBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(22, 255, 255, 255))
            try {
                $graphics.SetClip($backgroundPath)
                $graphics.FillEllipse(
                    $highlightBrush,
                    $canvas * 0.16,
                    -($canvas * 0.08),
                    $canvas * 0.68,
                    $canvas * 0.34
                )
                $graphics.ResetClip()
            }
            finally {
                $highlightBrush.Dispose()
            }

            $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(24, 255, 255, 255), [math]::Max(1.0, $canvas * 0.010))
            try {
                $graphics.DrawPath($borderPen, $backgroundPath)
            }
            finally {
                $borderPen.Dispose()
            }
        }
        finally {
            $backgroundPath.Dispose()
        }

        $leftCenter = [System.Drawing.PointF]::new($canvas * 0.29, $canvas * 0.56)
        $rightCenter = [System.Drawing.PointF]::new($canvas * 0.71, $canvas * 0.44)
        $nodeDiameter = $canvas * 0.21

        $leftGlow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(52, 77, 141, 255))
        $rightGlow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(52, 229, 154, 59))
        try {
            $glowScale = 1.38
            $glowDiameter = $nodeDiameter * $glowScale
            $graphics.FillEllipse($leftGlow, $leftCenter.X - ($glowDiameter / 2), $leftCenter.Y - ($glowDiameter / 2), $glowDiameter, $glowDiameter)
            $graphics.FillEllipse($rightGlow, $rightCenter.X - ($glowDiameter / 2), $rightCenter.Y - ($glowDiameter / 2), $glowDiameter, $glowDiameter)
        }
        finally {
            $leftGlow.Dispose()
            $rightGlow.Dispose()
        }

        $leftBrush = New-Object System.Drawing.SolidBrush((Get-Color '#4D8DFF'))
        $rightBrush = New-Object System.Drawing.SolidBrush((Get-Color '#E59A3B'))
        $nodeStroke = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 255, 255, 255), [math]::Max(1.0, $canvas * 0.012))
        try {
            $graphics.FillEllipse($leftBrush, $leftCenter.X - ($nodeDiameter / 2), $leftCenter.Y - ($nodeDiameter / 2), $nodeDiameter, $nodeDiameter)
            $graphics.FillEllipse($rightBrush, $rightCenter.X - ($nodeDiameter / 2), $rightCenter.Y - ($nodeDiameter / 2), $nodeDiameter, $nodeDiameter)
            $graphics.DrawEllipse($nodeStroke, $leftCenter.X - ($nodeDiameter / 2), $leftCenter.Y - ($nodeDiameter / 2), $nodeDiameter, $nodeDiameter)
            $graphics.DrawEllipse($nodeStroke, $rightCenter.X - ($nodeDiameter / 2), $rightCenter.Y - ($nodeDiameter / 2), $nodeDiameter, $nodeDiameter)
        }
        finally {
            $leftBrush.Dispose()
            $rightBrush.Dispose()
            $nodeStroke.Dispose()
        }

        $nodeHighlightBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(75, 255, 255, 255))
        try {
            $highlightDiameter = $nodeDiameter * 0.36
            $graphics.FillEllipse($nodeHighlightBrush, $leftCenter.X - ($nodeDiameter * 0.15), $leftCenter.Y - ($nodeDiameter * 0.29), $highlightDiameter, $highlightDiameter)
            $graphics.FillEllipse($nodeHighlightBrush, $rightCenter.X - ($nodeDiameter * 0.15), $rightCenter.Y - ($nodeDiameter * 0.29), $highlightDiameter, $highlightDiameter)
        }
        finally {
            $nodeHighlightBrush.Dispose()
        }

        $wavePath = New-Object System.Drawing.Drawing2D.GraphicsPath
        try {
            $wavePath.AddBezier(
                [System.Drawing.PointF]::new($canvas * 0.36, $canvas * 0.59),
                [System.Drawing.PointF]::new($canvas * 0.44, $canvas * 0.29),
                [System.Drawing.PointF]::new($canvas * 0.50, $canvas * 0.30),
                [System.Drawing.PointF]::new($canvas * 0.55, $canvas * 0.50)
            )
            $wavePath.AddBezier(
                [System.Drawing.PointF]::new($canvas * 0.55, $canvas * 0.50),
                [System.Drawing.PointF]::new($canvas * 0.61, $canvas * 0.74),
                [System.Drawing.PointF]::new($canvas * 0.65, $canvas * 0.66),
                [System.Drawing.PointF]::new($canvas * 0.66, $canvas * 0.45)
            )

            $shadowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(72, 0, 0, 0), [math]::Max(2.0, $canvas * 0.102))
            $shadowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $shadowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $shadowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

            $accentPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(115, 118, 224, 255), [math]::Max(2.0, $canvas * 0.092))
            $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $accentPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

            $wavePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(245, 255, 255, 255), [math]::Max(2.0, $canvas * 0.066))
            $wavePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $wavePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $wavePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

            try {
                $graphics.DrawPath($shadowPen, $wavePath)
                $graphics.DrawPath($accentPen, $wavePath)
                $graphics.DrawPath($wavePen, $wavePath)
            }
            finally {
                $shadowPen.Dispose()
                $accentPen.Dispose()
                $wavePen.Dispose()
            }
        }
        finally {
            $wavePath.Dispose()
        }

        $sparkBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(170, 255, 255, 255))
        try {
            $spark = $canvas * 0.045
            $graphics.FillEllipse($sparkBrush, $canvas * 0.61, $canvas * 0.60, $spark, $spark)
        }
        finally {
            $sparkBrush.Dispose()
        }

        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-PngAsIcon {
    param(
        [string]$PngPath,
        [string]$OutputPath
    )

    $source = [System.Drawing.Image]::FromFile($PngPath)
    $bitmap = New-Object System.Drawing.Bitmap(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($source, 0, 0, 256, 256)

        $iconHandle = $bitmap.GetHicon()
        try {
            $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
            try {
                $fileStream = [System.IO.File]::Create($OutputPath)
                try {
                    $icon.Save($fileStream)
                }
                finally {
                    $fileStream.Dispose()
                }
            }
            finally {
                $icon.Dispose()
            }
        }
        finally {
            [NativeIconMethods]::DestroyIcon($iconHandle) | Out-Null
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        $source.Dispose()
    }
}

$assetsDirectory = Join-Path $ProjectRoot 'src\P2PAudio.Windows.App\Assets'
New-Item -ItemType Directory -Force -Path $assetsDirectory | Out-Null

$previewPath = Join-Path $assetsDirectory 'AppIcon.png'
Draw-AppIcon -Size 1024 -OutputPath $previewPath
Save-PngAsIcon -PngPath $previewPath -OutputPath (Join-Path $assetsDirectory 'AppIcon.ico')
