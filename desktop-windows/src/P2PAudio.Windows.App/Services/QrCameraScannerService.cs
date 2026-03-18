using System.Drawing;
using System.Drawing.Imaging;
using Windows.Media.Capture;
using ZXing;
using ZXing.Windows.Compatibility;

namespace P2PAudio.Windows.App.Services;

public static class QrCameraScannerService
{
    public static async Task<string?> ScanAsync()
    {
        var ui = new CameraCaptureUI();
        ui.PhotoSettings.AllowCropping = false;
        ui.PhotoSettings.Format = CameraCaptureUIPhotoFormat.Jpeg;
        ui.PhotoSettings.MaxResolution = CameraCaptureUIMaxPhotoResolution.MediumXga;

        var file = await ui.CaptureFileAsync(CameraCaptureUIMode.Photo);
        if (file is null)
        {
            return null;
        }

        await using var fileStream = await file.OpenStreamForReadAsync();
        using var bitmap = new Bitmap(fileStream);
        var reader = new BarcodeReader
        {
            AutoRotate = true,
            Options =
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE]
            }
        };
        var result = reader.Decode(bitmap);
        return result?.Text;
    }
}
