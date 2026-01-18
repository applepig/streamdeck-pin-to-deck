# Generate white monochrome PNG icons for Stream Deck
Add-Type -AssemblyName System.Drawing

$sourceImage = "src\images\icon.png"
$outputDir = "src\PinToDeck\Images"

# Icon sizes needed
$icons = @{
    "pluginIcon.png"      = 256      # Plugin icon (Preferences pane) - requires PNG
    "pluginIcon@2x.png"   = 512   # Plugin icon @2x
    "actionIcon.png"      = 72       # Fallback for action list (prefer SVG)
    "actionIcon@2x.png"   = 144   # Fallback @2x
    "categoryIcon.png"    = 28     # Fallback for category (prefer SVG)
    "categoryIcon@2x.png" = 56  # Fallback @2x
}

Write-Host "Generating WHITE monochrome PNG icons for Stream Deck..."
Write-Host ""

# Load source image
$source = [System.Drawing.Image]::FromFile((Resolve-Path $sourceImage))

foreach ($iconName in $icons.Keys) {
    $size = $icons[$iconName]
    $outputPath = Join-Path $outputDir $iconName
    
    Write-Host "Creating ${iconName} (${size}x${size})..."
    
    # Create new bitmap
    $resized = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($resized)
    
    # Use high quality settings
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    
    # Draw resized image
    $graphics.DrawImage($source, 0, 0, $size, $size)
    $graphics.Dispose()
    
    # Convert to white (keep alpha channel)
    for ($x = 0; $x -lt $size; $x++) {
        for ($y = 0; $y -lt $size; $y++) {
            $pixel = $resized.GetPixel($x, $y)
            # If pixel is not transparent, make it white
            if ($pixel.A -gt 0) {
                $newPixel = [System.Drawing.Color]::FromArgb($pixel.A, 255, 255, 255)
                $resized.SetPixel($x, $y, $newPixel)
            }
        }
    }
    
    # Save
    $resized.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $resized.Dispose()
}

$source.Dispose()

Write-Host ""
Write-Host "All WHITE monochrome icons generated successfully!"
Write-Host "Stream Deck will colorize these automatically."
Write-Host "Output directory: $outputDir"
