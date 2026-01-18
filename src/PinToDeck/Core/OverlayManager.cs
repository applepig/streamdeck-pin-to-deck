using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace PinToDeck.Core
{
    public class OverlayManager
    {
        private static readonly Lazy<OverlayManager> _instance = new Lazy<OverlayManager>(() => new OverlayManager());
        public static OverlayManager Instance => _instance.Value;

        private NotificationForm? _form;
        private Thread? _uiThread;
        private readonly object _lock = new object();

        private OverlayManager() { }

        public void Show(string text)
        {
            lock (_lock)
            {
                if (_uiThread == null || !_uiThread.IsAlive)
                {
                    _uiThread = new Thread(() =>
                    {
                        Application.EnableVisualStyles();
                        _form = new NotificationForm();
                        Application.Run(_form);
                    });
                    _uiThread.SetApartmentState(ApartmentState.STA);
                    _uiThread.IsBackground = true;
                    _uiThread.Start();

                    // Wait for form handle to be created to avoid race conditions
                    // Simple spin wait or event could work, but for simplicity here we let the message pump handle it naturally
                }
            }

            // Dispatch to UI thread
            if (_form != null)
            {
                // We might need to wait slightly if the form is just starting up
                // But generally InvokeRequired handles checking handle creation.
                // If handle not created yet, we might miss the very first update or crash.
                // Let's add a small retrying mechanism or event.

                // For robustness, simply posting to the SynchronizationContext is better, 
                // but since we are cross-thread, we use Invoke.

                // Retry loop for valid handle
                int retry = 0;
                while ((_form == null || !_form.IsHandleCreated) && retry < 10)
                {
                    Thread.Sleep(10);
                    retry++;
                }

                if (_form != null && _form.IsHandleCreated)
                {
                    _form.BeginInvoke(new Action(() => _form.ShowNotification(text)));
                }
            }
        }
    }

    internal class NotificationForm : Form
    {
        private Label _lblText;
        private Timer _timer;
        private const int DISPLAY_DURATION_MS = 1500;
        private const int FADE_DURATION_MS = 500;
        // Native constants
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020; // Click through

        public NotificationForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(600, 100); // Fixed size width
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black; // Simple transparency for non-layered, but we want alpha blending
                                                // Actually for good OSD we want CreateParams to handle Layered, but using BackColor+TransparencyKey is easiest for "Shaped non-translucent text".
                                                // If we want semi-transparent background, we need UpdateLayeredWindow (complex).
                                                // Let's stick to "Round Rect Dark Panel" using standard painting.

            // To support actual semi-transparency we can't use TransparencyKey easily with controls.
            // Let's maintain simple: Black background -> rounded region?
            // Or just a clean dark grey box.

            this.DoubleBuffered = true;
            this.TopMost = true;

            _lblText = new Label();
            _lblText.Dock = DockStyle.Fill;
            _lblText.TextAlign = ContentAlignment.MiddleCenter;
            _lblText.ForeColor = Color.White;
            _lblText.Font = new Font("Segoe UI", 16, FontStyle.Bold);
            _lblText.BackColor = Color.FromArgb(40, 40, 40); // Dark Grey
            this.Controls.Add(_lblText);

            _timer = new Timer();
            _timer.Interval = DISPLAY_DURATION_MS;
            _timer.Tick += (s, e) => { HideOverlay(); };

            // Set initial position
            InitializePosition();
        }

        private void InitializePosition()
        {
            // Position at bottom center of primary screen
            var screen = Screen.PrimaryScreen?.Bounds ?? (Screen.AllScreens.Length > 0 ? Screen.AllScreens[0].Bounds : new Rectangle(0, 0, 1920, 1080));
            int x = (screen.Width - this.Width) / 2;
            int y = screen.Height - 200; // 200px from bottom
            this.Location = new Point(x, y);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Optional: Draw border
            ControlPaint.DrawBorder(e.Graphics, this.ClientRectangle, Color.FromArgb(100, 100, 100), ButtonBorderStyle.Solid);
        }

        public void ShowNotification(string text)
        {
            // If we are hidden, show
            if (!this.Visible)
            {
                // Ensure we don't steal focus even when showing
                NativeMethods.ShowWindow(this.Handle, NativeMethods.SW_SHOWNOACTIVATE);
                this.Visible = true;
            }

            _lblText.Text = text;
            _timer.Stop();
            _timer.Start();

            // Re-center if text is very long? 
            // Current implementation has fixed width. 
            // Let's just keep it simple.
        }

        private void HideOverlay()
        {
            this.Visible = false;
            _timer.Stop();
        }
    }
}
