$exePath = "C:\Users\nmrhr\Documents\cross-platform\bok\desktop-windows\src\P2PAudio.Windows.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\P2PAudio.Windows.App.exe"
if (Test-Path $exePath) {
    Write-Host "Starting app..."
    $proc = Start-Process $exePath -PassThru
    Write-Host "Process ID: $($proc.Id)"
} else {
    Write-Host "EXE not found at: $exePath"
}