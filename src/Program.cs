using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Klee Codex Quota Widget")]
[assembly: System.Reflection.AssemblyDescription("Windows 11 taskbar widget for Codex quota and task status")]
[assembly: System.Reflection.AssemblyCompany("Klee")]
[assembly: System.Reflection.AssemblyProduct("Klee Codex Quota Widget")]
[assembly: System.Reflection.AssemblyVersion("1.5.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.5.0.0")]

namespace KleeCodexQuotaWidget
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try { NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            if (args.Length == 2 && args[0] == "--verify-status")
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { using (var form = new QuotaForm()) form.VerifyStatus(args[1]); }
                catch(Exception ex)
                {
                    Directory.CreateDirectory(args[1]);
                    File.WriteAllText(Path.Combine(args[1],"failure.txt"),ex.ToString());
                    Environment.ExitCode = 1;
                }
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\KleeCodexQuotaWidget", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new QuotaForm());
            }
        }
    }

    // This host owns its layout. ToolStrip's automatic DPI scaling must not leave
    // pixel-sized children behind when only the inherited font changes.
    internal sealed class ScalePanel : Panel
    {
        internal readonly Label Caption = new Label { AutoSize = false };
        internal readonly Label Hint = new Label { AutoSize = false, Text = "50% – 200% · 自动适配任务栏" };
        internal readonly Button Reset = new Button { Text = "恢复默认", FlatStyle = FlatStyle.Flat };
        internal readonly ScaleSlider Slider = new ScaleSlider { AccessibleName = "界面缩放百分比" };
        private float scale = 1f;
        private bool arranging;
        internal ScalePanel(int value)
        {
            Margin = Padding.Empty;
            Reset.FlatAppearance.BorderSize = 0;
            Slider.Value = value;
            Caption.Text = "界面缩放   " + value + "%";
            Controls.AddRange(new Control[] { Caption, Reset, Slider, Hint });
            Slider.ValueChanged += delegate { Caption.Text = "界面缩放   " + Slider.Value + "%"; };
            Reset.Click += delegate { Slider.Value = 100; };
        }
        internal void Arrange(float dpiScale, int width)
        {
            if (arranging) return;
            arranging = true;
            try
            {
                scale = dpiScale;
                int pad = Math.Max(6, (int)Math.Ceiling(10 * scale));
                int gap = Math.Max(4, (int)Math.Ceiling(8 * scale));
                int inner = Math.Max(1, width - pad * 2);
                Size caption = TextRenderer.MeasureText("界面缩放   200%", Font);
                Size reset = TextRenderer.MeasureText(Reset.Text, Font);
                int buttonWidth = reset.Width + pad * 2;
                int rowHeight = Math.Max(caption.Height, reset.Height) + gap;
                int y = pad;
                if (caption.Width + buttonWidth + gap <= inner)
                {
                    Caption.SetBounds(pad,y,inner-buttonWidth-gap,rowHeight);
                    Reset.SetBounds(width-pad-buttonWidth,y,buttonWidth,rowHeight);
                    y += rowHeight + gap;
                }
                else
                {
                    Size wrapped = TextRenderer.MeasureText(Caption.Text, Font, new Size(inner,Int32.MaxValue), TextFormatFlags.WordBreak);
                    Caption.SetBounds(pad,y,inner,wrapped.Height+gap);
                    y += Caption.Height;
                    Reset.SetBounds(pad,y,Math.Min(inner,buttonWidth),rowHeight);
                    y += rowHeight + gap;
                }
                Slider.UiScale = scale;
                Slider.SetBounds(pad,y,inner,Math.Max(rowHeight,(int)Math.Ceiling(30*scale)));
                y += Slider.Height + gap;
                Size hint = TextRenderer.MeasureText(Hint.Text, Font, new Size(inner,Int32.MaxValue), TextFormatFlags.WordBreak);
                Hint.SetBounds(pad,y,inner,hint.Height+gap);
                Size = new Size(width,Hint.Bottom+pad);
            }
            finally { arranging = false; }
        }
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (Slider != null && Width > 0) Arrange(scale,Width);
        }
        internal void VerifyLayout()
        {
            foreach (Control c in Controls)
                if (!ClientRectangle.Contains(c.Bounds)) throw new Exception("Scale control outside panel: " + c.GetType().Name);
            if (Caption.Bounds.IntersectsWith(Reset.Bounds) || Reset.Bottom > Slider.Top || Slider.Bottom > Hint.Top)
                throw new Exception("Scale controls overlap");
            foreach (Label label in new[] { Caption, Hint })
            {
                Size needed = TextRenderer.MeasureText(label.Text,Font,new Size(label.Width,Int32.MaxValue),TextFormatFlags.WordBreak);
                if (needed.Height > label.Height) throw new Exception("Scale label clipped: " + label.Text);
            }
            Size resetSize=TextRenderer.MeasureText(Reset.Text,Font);
            if (resetSize.Height > Reset.Height || resetSize.Width > Reset.Width) throw new Exception("Reset button clipped");
        }
    }

    internal sealed class AdaptiveMenu : ContextMenuStrip
    {
        internal event EventHandler MonitorDpiChanged;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x02E0 && IsHandleCreated && !IsDisposed)
                BeginInvoke((MethodInvoker)delegate {
                    if (!IsDisposed && MonitorDpiChanged != null) MonitorDpiChanged(this,EventArgs.Empty);
                });
        }
    }


    internal sealed class ScaleSlider : Control
    {
        private int value = 100;
        internal float UiScale = 1f;
        private int Inset { get { return Math.Max(4,(int)Math.Ceiling(12*UiScale)); } }
        public event EventHandler ValueChanged;
        public int Value { get { return value; } set {
            int next = Math.Max(50, Math.Min(200,value));
            if (this.value == next) return;
            this.value = next; Invalidate();
            if (ValueChanged != null) ValueChanged(this,EventArgs.Empty);
        } }
        public ScaleSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true; AccessibleRole = AccessibleRole.Slider;
        }
        private void SetFromX(int x) { Value = 50 + (int)Math.Round(150.0 * (x-Inset) / Math.Max(1,Width-Inset*2)); }
        internal void VerifyInput()
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,Inset,Height/2,0));
            if(Value!=50) throw new Exception("Slider left endpoint");
            OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,Width-Inset,Height/2,0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,Width-Inset,Height/2,0));
            if(Value!=200 || Capture) throw new Exception("Slider right endpoint/capture");
            OnKeyDown(new KeyEventArgs(Keys.Left));
            if(Value!=199) throw new Exception("Slider keyboard");
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(e.Button==MouseButtons.Left) { Focus(); Capture=true; SetFromX(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(Capture) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if(e.Button==MouseButtons.Left) Capture=false; }
        protected override bool IsInputKey(Keys keyData) { return keyData==Keys.Left || keyData==Keys.Right || keyData==Keys.Home || keyData==Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if(e.KeyCode==Keys.Left) Value--; else if(e.KeyCode==Keys.Right) Value++;
            else if(e.KeyCode==Keys.Home) Value=50; else if(e.KeyCode==Keys.End) Value=200;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int y=Height/2; float x=Inset+(Width-Inset*2)*(Value-50)/150f;
            float radius=7*UiScale;
            using(var track=new Pen(BackColor.R>128 ? Color.FromArgb(200,200,200) : Color.FromArgb(85,85,85),4*UiScale))
            using(var fill=new Pen(Color.FromArgb(80,160,245),4*UiScale))
            using(var thumb=new SolidBrush(Color.FromArgb(100,180,255)))
            {
                track.StartCap=track.EndCap=fill.StartCap=fill.EndCap=System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(track,Inset,y,Width-Inset,y); g.DrawLine(fill,Inset,y,x,y);
                g.FillEllipse(thumb,x-radius,y-radius,radius*2,radius*2);
            }
            if(Focused) ControlPaint.DrawFocusRectangle(g,ClientRectangle);
        }
    }

    internal sealed class WidgetMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool light;
        public WidgetMenuRenderer(bool light) { this.light = light; RoundedEdges = true; }
        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(e.ToolStrip.BackColor);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            using (var brush = new SolidBrush(light ? Color.FromArgb(233,233,233) : Color.FromArgb(58,58,58)))
            {
                int d = 8;
                path.AddArc(rect.Left, rect.Top, d,d,180,90);
                path.AddArc(rect.Right-d, rect.Top,d,d,270,90);
                path.AddArc(rect.Right-d, rect.Bottom-d,d,d,0,90);
                path.AddArc(rect.Left, rect.Bottom-d,d,d,90,90);
                path.CloseFigure();
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush,path);
            }
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(light ? Color.FromArgb(225,225,225) : Color.FromArgb(65,65,65)))
                e.Graphics.DrawLine(pen, 10, e.Item.Height/2, e.Item.Width-10, e.Item.Height/2);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(light ? Color.FromArgb(220,220,220) : Color.FromArgb(70,70,70)))
                e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
        }
    }

    internal sealed class LimitReading
    {
        public int? Remaining;
        public DateTimeOffset? ResetsAt;
    }

    internal sealed class UsageSnapshot
    {
        public LimitReading FiveHour = new LimitReading();
        public LimitReading Weekly = new LimitReading();
        public int? ResetCreditsAvailable;
        public DateTimeOffset RefreshedAt;
        public string Error;
    }

    internal sealed class TiboEvent
    {
        public string Id;
        public string Text;
        public string SourceUrl;
        public DateTimeOffset PostedAt;
        public DateTimeOffset TargetAt;
    }

    internal enum CodexTaskState
    {
        Hidden,
        Running,
        Completed,
        Waiting,
        Interrupted
    }

    internal sealed class QuotaForm : Form
    {
        private const int DefaultLeftOffset = 8;
        private const int CompactWidth = 292;
        private const int StatusWidth = 390;
        private const int CountdownWidth = 432;
        private const int StatusExtraWidth = 24;
        private const int WidgetHeight = 34;
        private const int ActivePollSeconds = 30;
        private const int IdlePollSeconds = 60;
        private const int ActivityDebounceSeconds = 3;
        private const int MinimumActivityPollGapSeconds = 8;
        private const string StartupName = "KleeCodexQuotaWidget";
        private const string SettingsKey = @"Software\Klee\CodexQuotaWidget";

        private readonly System.Windows.Forms.Timer uiTimer;
        private readonly System.Windows.Forms.Timer animationTimer;
        private readonly AdaptiveMenu menu;
        private ScalePanel scalePanel;
        private ToolStripControlHost scaleHost;
        private Font menuFont;
        private bool verifyingMenu;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem freshnessItem;
        private readonly NotifyIcon trayIcon;
        private readonly Icon trayIconImage;
        private UsageSnapshot snapshot = UsageCache.Load();
        private TiboEvent tiboEvent;
        private DateTimeOffset? quotaNextPoll;
        private DateTimeOffset? tiboNextPoll;
        private DateTimeOffset? deliveredUntil;
        private int? beforeEventFiveHour;
        private int? beforeEventWeekly;
        private bool quotaPolling;
        private int quotaFailures;
        private bool tiboPolling;
        private DateTimeOffset? lastCodexActivity;
        private DateTimeOffset? activityRefreshDue;
        private DateTimeOffset? lastQuotaAttempt;
        private DateTimeOffset? positionNextCheck;
        private FileSystemWatcher sessionsWatcher;
        private FileSystemWatcher authWatcher;
        private readonly object sessionStateLock = new object();
        private readonly Dictionary<string, long> sessionOffsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> sessionPending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> sessionReadsInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> sessionReadAgain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeTaskSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> waitingSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> interruptedSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> inputCalls = new Dictionary<string, string>();
        private bool wasStale;
        private string paintedRightStatus;
        private float displayScale = 1f;
        private int userScalePercent = 100;
        private string monitorDevice;
        private CodexTaskState taskState = CodexTaskState.Hidden;
        private int spinnerAngle;
        private readonly Bitmap knotLogo;
        private IntPtr taskbarHandle = IntPtr.Zero;
        private int leftOffset;
        private bool dragging;
        private Point dragStart;
        private int dragOffsetStart;
        private readonly Font mainFont;
        private readonly Font smallFont;
        private Bitmap frame;
        private readonly LogoInputWindow logoInput = new LogoInputWindow();
        private Color background;
        private Color foreground;

        public QuotaForm()
        {
            leftOffset = ReadLeftOffset();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                    if (key != null) userScalePercent = Math.Max(50, Math.Min(200, Convert.ToInt32(key.GetValue("ScalePercent", 100))));
            }
            catch { userScalePercent = 100; }
            using (var stream = typeof(QuotaForm).Assembly.GetManifestResourceStream("CodexKnot.png"))
            using (var source = Image.FromStream(stream))
                knotLogo = new Bitmap(source);
            mainFont = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            smallFont = new Font("Segoe UI", 8.4f, FontStyle.Regular, GraphicsUnit.Point);

            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(CompactWidth + StatusExtraWidth, WidgetHeight);
            DoubleBuffered = true;
            TopMost = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            menu = new AdaptiveMenu();
            menu.Padding = new Padding(8);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Items.Add(new ToolStripMenuItem("CODEX  ·  额度监控") { Enabled = false });
            freshnessItem = new ToolStripMenuItem("尚未取得服务器数据");
            freshnessItem.Enabled = false;
            menu.Items.Add(freshnessItem);
            menu.Items.Add(new ToolStripSeparator());
            scaleHost = new ToolStripControlHost(BuildScaleControl()) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false };
            menu.Items.Add(scaleHost);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("↻   立即刷新", null, delegate { ForceRefresh(); });
            var displays = new ToolStripMenuItem("显示位置");
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                monitorDevice = key == null ? null : key.GetValue("MonitorDevice") as string;
            foreach (Screen screen in Screen.AllScreens)
            {
                string device = screen.DeviceName;
                displays.DropDownItems.Add(device + (screen.Primary ? "（主屏）" : ""), null, delegate
                {
                    monitorDevice = device;
                    using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey)) key.SetValue("MonitorDevice", device);
                    taskbarHandle = IntPtr.Zero;
                    AttachAndPosition();
                });
            }
            menu.Items.Add(displays);
            menu.Items.Add("查看 Tibo 监控源", null, delegate { OpenUrl("https://codexreset.org/zh/"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("恢复左下角位置", null, delegate { leftOffset = DefaultLeftOffset; SaveLeftOffset(); AttachAndPosition(); });
            startupItem = new ToolStripMenuItem("开机自启动");
            startupItem.Checked = IsStartupEnabled();
            startupItem.Click += delegate { SetStartup(!IsStartupEnabled()); startupItem.Checked = IsStartupEnabled(); };
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            foreach (ToolStripItem item in menu.Items)
                if (item is ToolStripMenuItem) item.Padding = new Padding(10, 6, 10, 6);
            menu.Opening += delegate { UpdateFreshnessText(); ApplyTheme(); PrepareMenuForMonitor(false); startupItem.Checked = IsStartupEnabled(); };
            menu.MonitorDpiChanged += delegate { if(menu.Visible && !verifyingMenu) PrepareMenuForMonitor(true); };
            menu.Closing += delegate(object sender, ToolStripDropDownClosingEventArgs e) {
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && menu.GetItemAt(menu.PointToClient(Cursor.Position)) is ToolStripControlHost)
                    e.Cancel = true;
            };
            menu.Closed += delegate {
                if (verifyingMenu) return;
                try { using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("ScalePercent", userScalePercent, RegistryValueKind.DWord); } catch { }
            };
            menu.Opened += delegate {
                if (!verifyingMenu) PrepareMenuForMonitor(true);
                try { int corner = 2; WidgetMenuRenderer.DwmSetWindowAttribute(menu.Handle, 33, ref corner, 4); } catch { }
            };
            logoInput.ContextMenuStrip = menu;

            try
            {
                trayIconImage = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (trayIconImage == null) trayIconImage = (Icon)SystemIcons.Application.Clone();
            }
            catch { trayIconImage = (Icon)SystemIcons.Application.Clone(); }
            trayIcon = new NotifyIcon();
            trayIcon.Icon = trayIconImage;
            trayIcon.Text = "Codex 额度组件";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += delegate { ForceRefresh(); };

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 250;
            uiTimer.Tick += UiTick;

            animationTimer = new System.Windows.Forms.Timer();
            animationTimer.Interval = 33;
            animationTimer.Tick += AnimationTick;

            logoInput.MouseDown += OnWidgetMouseDown;
            logoInput.MouseMove += OnWidgetMouseMove;
            logoInput.MouseUp += OnWidgetMouseUp;
            logoInput.MouseDoubleClick += delegate { ForceRefresh(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | 0x00080000 | 0x00000020;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyTheme();
            AttachAndPosition();
            RebuildFrame();
            trayIcon.Visible = true;
            StartActivityWatchers();
            uiTimer.Start();
            ForceRefresh();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            uiTimer.Stop();
            animationTimer.Stop();
            if (sessionsWatcher != null) sessionsWatcher.Dispose();
            if (authWatcher != null) authWatcher.Dispose();
            mainFont.Dispose();
            smallFont.Dispose();
            knotLogo.Dispose();
            logoInput.Dispose();
            if (frame != null) frame.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIconImage.Dispose();
            menu.Dispose();
            if(menuFont != null) menuFont.Dispose();
            animationTimer.Dispose();
            base.OnFormClosed(e);
        }

        private void UiTick(object sender, EventArgs e)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            AttachAndPosition();
            if (wasStale != IsQuotaStale()) { wasStale = IsQuotaStale(); RebuildFrame(); }
            // Window and menu mutations are deliberately kept out of the one-second
            // countdown clock. Even a hidden ToolStrip relayout can make WinForms/DWM
            // briefly recompose this topmost taskbar overlay.
            if (!positionNextCheck.HasValue || now >= positionNextCheck.Value)
            {
                AttachAndPosition();
                Color previousTheme = background;
                ApplyTheme();
                if (previousTheme != background) RebuildFrame();
                positionNextCheck = now.AddSeconds(15);
            }
            bool awaiting = tiboEvent != null && now >= tiboEvent.TargetAt && now < tiboEvent.TargetAt.AddHours(2);
            if (activityRefreshDue.HasValue && now >= activityRefreshDue.Value && !quotaPolling &&
                (!lastQuotaAttempt.HasValue || now - lastQuotaAttempt.Value >= TimeSpan.FromSeconds(MinimumActivityPollGapSeconds)))
            {
                activityRefreshDue = null;
                PollQuota(GetNextQuotaInterval(awaiting));
            }
            if (!quotaPolling && (!quotaNextPoll.HasValue || now >= quotaNextPoll.Value))
                PollQuota(GetNextQuotaInterval(awaiting));
            if (!tiboPolling && (!tiboNextPoll.HasValue || now >= tiboNextPoll.Value))
                PollTibo();
            if (tiboEvent != null && now > tiboEvent.TargetAt.AddHours(2))
                tiboEvent = null;
            if (deliveredUntil.HasValue && now > deliveredUntil.Value) deliveredUntil = null;
            UpdateWidth();
            // A visible Tibo countdown is the only right-side status that changes every second.
            // Avoid repainting static quota text continuously, which can flicker on the taskbar.
            string currentStatus = FormatRightStatus();
            if (paintedRightStatus != currentStatus) { paintedRightStatus = currentStatus; RebuildFrame(); }
        }

        private void ForceRefresh()
        {
            quotaNextPoll = DateTimeOffset.MinValue;
            tiboNextPoll = DateTimeOffset.MinValue;
            if (!quotaPolling) PollQuota(GetNextQuotaInterval(false));
            if (!tiboPolling) PollTibo();
        }

        private async void PollQuota(int nextSeconds)
        {
            quotaPolling = true;
            lastQuotaAttempt = DateTimeOffset.Now;
            try
            {
                bool needResetCredits = snapshot.Weekly.Remaining.HasValue && snapshot.Weekly.Remaining.Value < 20;
                UsageSnapshot next = await Task.Run(delegate { return CodexRateLimitReader.Fetch(needResetCredits); });
                DetectDelivery(next);
                UsageCache.Save(next);
                snapshot = next;
                quotaFailures = 0;
            }
            catch (Exception ex)
            {
                snapshot.Error = ex.Message;
                quotaFailures++;
                AppLog.Write("Quota refresh failed: " + ex);
            }
            finally
            {
                quotaPolling = false;
                int delay = quotaFailures == 0 ? nextSeconds :
                    Math.Min(nextSeconds, 10 * (1 << Math.Min(3, quotaFailures - 1)));
                quotaNextPoll = DateTimeOffset.Now.AddSeconds(delay);
                RebuildFrame();
            }
        }

        private int GetNextQuotaInterval(bool awaiting)
        {
            if (awaiting) return 15;
            if (lastCodexActivity.HasValue && DateTimeOffset.Now - lastCodexActivity.Value < TimeSpan.FromMinutes(10))
                return ActivePollSeconds;
            return IdlePollSeconds;
        }

        private void StartActivityWatchers()
        {
            try
            {
                string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (String.IsNullOrWhiteSpace(codexHome))
                    codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                string sessions = Path.Combine(codexHome, "sessions");
                if (Directory.Exists(sessions))
                {
                    foreach (string existing in Directory.GetFiles(sessions, "*.jsonl", SearchOption.AllDirectories))
                    {
                        try
                        {
                            long end = new FileInfo(existing).Length;
                            sessionOffsets[existing] = end;
                            RestoreTaskState(existing, end);
                        }
                        catch { }
                    }
                    sessionsWatcher = new FileSystemWatcher(sessions, "*.jsonl");
                    sessionsWatcher.IncludeSubdirectories = true;
                    sessionsWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                    sessionsWatcher.Changed += CodexActivityChanged;
                    sessionsWatcher.Created += CodexActivityChanged;
                    sessionsWatcher.Renamed += CodexActivityRenamed;
                    sessionsWatcher.EnableRaisingEvents = true;
                }
                if (Directory.Exists(codexHome))
                {
                    authWatcher = new FileSystemWatcher(codexHome, "auth.json");
                    authWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                    authWatcher.Changed += CodexActivityChanged;
                    authWatcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Activity watcher unavailable: " + ex.Message);
            }
        }

        private void CodexActivityChanged(object sender, FileSystemEventArgs e)
        {
            QueueSessionStateRead(e.FullPath);
            QueueActivityRefresh();
        }

        private void CodexActivityRenamed(object sender, RenamedEventArgs e)
        {
            QueueSessionStateRead(e.FullPath);
            QueueActivityRefresh();
        }

        private void QueueSessionStateRead(string path)
        {
            if (String.IsNullOrEmpty(path) || !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return;
            lock (sessionStateLock)
            {
                if (!sessionReadsInProgress.Add(path))
                {
                    sessionReadAgain.Add(path);
                    return;
                }
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string lastEvent = null;
                var events = new List<string>();
                bool readAgain = false;
                try
                {
                    long offset;
                    string pending;
                    lock (sessionStateLock)
                    {
                        if (!sessionOffsets.TryGetValue(path, out offset)) offset = 0;
                        if (!sessionPending.TryGetValue(path, out pending)) pending = String.Empty;
                    }
                    string appended;
                    long length;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (offset > stream.Length) offset = 0;
                        stream.Position = offset;
                        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, true))
                            appended = reader.ReadToEnd();
                        length = stream.Position;
                    }
                    string combined = pending + appended;
                    bool endsWithLine = combined.EndsWith("\n", StringComparison.Ordinal);
                    string[] lines = combined.Split('\n');
                    int completeCount = endsWithLine ? lines.Length : Math.Max(0, lines.Length - 1);
                    string nextPending = endsWithLine ? String.Empty : lines[lines.Length - 1];
                    for (int i = 0; i < completeCount; i++)
                    {
                        string line = lines[i].TrimEnd('\r');
                        string stateEvent = ReadStateEvent(line, path);
                        if (stateEvent != null) { lastEvent = stateEvent; events.Add(stateEvent); }
                    }
                    lock (sessionStateLock)
                    {
                        sessionOffsets[path] = length;
                        sessionPending[path] = nextPending;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (Exception ex) { AppLog.Write("Session status read failed: " + ex.Message); }
                finally
                {
                    lock (sessionStateLock)
                    {
                        sessionReadsInProgress.Remove(path);
                        readAgain = sessionReadAgain.Remove(path);
                    }
                }
                if (lastEvent != null)
                {
                    try { BeginInvoke((MethodInvoker)delegate { foreach (string stateEvent in events) ApplyTaskEvent(path, stateEvent); }); }
                    catch { }
                }
                if (readAgain) QueueSessionStateRead(path);
            });
        }

        private static bool IsTaskEvent(string line, string wanted)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(line) as Dictionary<string, object>;
                object outerType;
                object payloadValue;
                if (root == null || !root.TryGetValue("type", out outerType) ||
                    !String.Equals(Convert.ToString(outerType, CultureInfo.InvariantCulture), "event_msg", StringComparison.Ordinal) ||
                    !root.TryGetValue("payload", out payloadValue)) return false;
                var payload = payloadValue as Dictionary<string, object>;
                object eventType;
                return payload != null && payload.TryGetValue("type", out eventType) &&
                    String.Equals(Convert.ToString(eventType, CultureInfo.InvariantCulture), wanted, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private void ApplyTaskEvent(string path, string eventType)
        {
            if (eventType == "resumed")
            {
                if (!waitingSessions.Contains(path)) return;
                eventType = "task_started";
            }
            if (eventType == "task_started")
            {
                activeTaskSessions.Add(path);
                waitingSessions.Remove(path);
                interruptedSessions.Remove(path);
            }
            else if (eventType == "task_complete")
            {
                activeTaskSessions.Remove(path);
                waitingSessions.Remove(path);
            }
            else if (eventType == "waiting")
            {
                activeTaskSessions.Remove(path);
                waitingSessions.Add(path);
            }
            else if (eventType == "interrupted")
            {
                activeTaskSessions.Remove(path);
                waitingSessions.Remove(path);
                interruptedSessions.Add(path);
            }
            SetTaskState(activeTaskSessions.Count > 0 ? CodexTaskState.Running :
                waitingSessions.Count > 0 ? CodexTaskState.Waiting :
                interruptedSessions.Count > 0 ? CodexTaskState.Interrupted : CodexTaskState.Completed);
        }

        private void RestoreTaskState(string path, long end)
        {
            // Ignore history older than the currently running Codex processes.
            DateTime since = DateTime.UtcNow;
            bool found = false;
            foreach (var process in Process.GetProcessesByName("Codex"))
                using (process)
                    try { if (process.StartTime.ToUniversalTime() < since) { since = process.StartTime.ToUniversalTime(); found = true; } } catch { }
            if (!found || File.GetLastWriteTimeUtc(path) < since) return;
            string last = null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    // Only lifecycle records are needed for startup reconciliation.
                    if (line.IndexOf("event_msg", StringComparison.Ordinal) < 0 && line.IndexOf("response_item", StringComparison.Ordinal) < 0) continue;
                    try
                    {
                        var data = new JavaScriptSerializer().DeserializeObject(line) as Dictionary<string, object>;
                        DateTimeOffset stamp;
                        if (data == null || !data.ContainsKey("timestamp") || !DateTimeOffset.TryParse(Convert.ToString(data["timestamp"]), out stamp) || stamp.UtcDateTime < since) continue;
                        string state = ReadStateEvent(line, path);
                        if (state != null) last = state;
                    } catch { }
                }
            }
            // A historical completed task never adds a green badge on startup.
            if (last == "task_started" || last == "waiting")
            {
                ApplyTaskEvent(path, last);
                AppLog.Write("Restored active task state: " + last);
            }
        }

        private string ReadStateEvent(string line, string path)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(line) as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("payload")) return null;
                var p = root["payload"] as Dictionary<string, object>;
                if (p == null || !p.ContainsKey("type")) return null;
                string type = Convert.ToString(p["type"]);
                if (Convert.ToString(root["type"]) == "event_msg")
                {
                    if (type == "task_started") return "task_started";
                    if (type == "turn_aborted" || type == "task_failed") return "interrupted";
                    if (type == "error" && p.ContainsKey("will_retry") && Object.Equals(p["will_retry"], false)) return "interrupted";
                    if (type == "exec_command_begin" || type == "patch_apply_begin" || type == "request_user_input_response") return "resumed";
                    if (type == "exec_approval_request" || type == "apply_patch_approval_request" || type == "request_user_input") return "waiting";
                    if (type == "task_complete")
                    {
                        string status = p.ContainsKey("status") ? Convert.ToString(p["status"]) : "";
                        return status == "failed" || status == "interrupted" || status == "cancelled" ? "interrupted" : "task_complete";
                    }
                }
                if (Convert.ToString(root["type"]) == "response_item")
                {
                    string name = p.ContainsKey("name") ? Convert.ToString(p["name"]) : "";
                    string id = p.ContainsKey("call_id") ? Convert.ToString(p["call_id"]) : "";
                    lock (sessionStateLock)
                    {
                        if ((type == "function_call" || type == "custom_tool_call") && name.EndsWith("request_user_input", StringComparison.Ordinal))
                        { inputCalls[path + id] = id; return "waiting"; }
                        if ((type == "function_call_output" || type == "custom_tool_call_output") && inputCalls.Remove(path + id)) return "task_started";
                    }
                }
            } catch { }
            return null;
        }

        private bool IsQuotaStale()
        {
            return !String.IsNullOrEmpty(snapshot.Error) || DateTimeOffset.Now - snapshot.RefreshedAt > TimeSpan.FromMinutes(2);
        }

        internal void VerifyStatus(string output)
        {
            Directory.CreateDirectory(output);
            snapshot = new UsageSnapshot();
            ApplyTheme();
            snapshot.FiveHour.Remaining = 76;
            snapshot.Weekly.Remaining = 42;
            snapshot.RefreshedAt = DateTimeOffset.Now;
            snapshot.Error = null;
            RebuildFrame(false);
            frame.Save(Path.Combine(output, "idle.png"));
            if (taskState != CodexTaskState.Hidden || animationTimer.Enabled) throw new Exception("Initial state failed");
            ApplyTaskEvent("test-a", "task_started");
            if (taskState != CodexTaskState.Running || !animationTimer.Enabled) throw new Exception("Start failed");
            frame.Save(Path.Combine(output, "running.png"));
            using (var before = (Bitmap)frame.Clone())
            {
                AnimationTick(null, EventArgs.Empty);
                int iconChanges = 0;
                for (int y = 0; y < frame.Height; y++)
                    for (int x = 0; x < frame.Width; x++)
                        if (before.GetPixel(x, y) != frame.GetPixel(x, y))
                        {
                            if (x >= 30) throw new Exception("Animation modified quota pixels");
                            iconChanges++;
                        }
                if (iconChanges == 0) throw new Exception("Logo did not rotate");
            }
            ApplyTaskEvent("test-b", "task_started");
            ApplyTaskEvent("test-a", "task_complete");
            if (taskState != CodexTaskState.Running) throw new Exception("Parallel state failed");
            ApplyTaskEvent("test-b", "task_complete");
            if (taskState != CodexTaskState.Completed || animationTimer.Enabled || spinnerAngle != 0)
                throw new Exception("Completion failed");
            frame.Save(Path.Combine(output, "completed.png"));
            ApplyTaskEvent("test-a", "task_started");
            ApplyTaskEvent("test-a", "waiting");
            if (taskState != CodexTaskState.Waiting || animationTimer.Enabled) throw new Exception("Waiting failed");
            frame.Save(Path.Combine(output, "waiting.png"));
            ApplyTaskEvent("test-a", "task_started");
            if (taskState != CodexTaskState.Running) throw new Exception("Resume failed");
            ApplyTaskEvent("test-a", "interrupted");
            ApplyTaskEvent("test-a", "task_complete");
            if (taskState != CodexTaskState.Interrupted || animationTimer.Enabled) throw new Exception("Interrupted task falsely completed");
            frame.Save(Path.Combine(output, "interrupted.png"));
            snapshot.RefreshedAt = DateTimeOffset.Now.AddMinutes(-3);
            if (!IsQuotaStale() || QuotaColor(76) != Muted()) throw new Exception("Stale values not muted");
            RebuildFrame(false);
            frame.Save(Path.Combine(output, "stale.png"));
            background = Color.FromArgb(243,243,243); foreground = Color.FromArgb(28,28,28);
            RebuildFrame(false);
            frame.Save(Path.Combine(output, "light.png"));
            if (ReadStateEvent("{\"type\":\"event_msg\",\"payload\":{\"type\":\"turn_aborted\"}}", "test") != "interrupted") throw new Exception("Abort parse failed");
            snapshot.RefreshedAt = DateTimeOffset.Now;
            snapshot.Error = null;
            snapshot.Weekly.Remaining = 0;
            snapshot.ResetCreditsAvailable = 2;
            deliveredUntil = null;
            tiboEvent = null;
            if (FormatRightStatus() != "可重置 ×2") throw new Exception("Reset opportunity label failed");
            RebuildFrame(false);
            frame.Save(Path.Combine(output, "reset-available.png"));
            snapshot.Weekly.Remaining = 1;
            if (FormatRightStatus() != String.Empty) throw new Exception("Reset threshold boundary failed");
            snapshot.Weekly.Remaining = 0;
            snapshot.ResetCreditsAvailable = 0;
            if (FormatRightStatus() != String.Empty) throw new Exception("Zero reset credit should stay hidden");
            snapshot.ResetCreditsAvailable = 2;
            snapshot.Weekly.Remaining = 15;
            tiboEvent = new TiboEvent { TargetAt = DateTimeOffset.Now.AddHours(1) };
            if (!FormatRightStatus().StartsWith("Tibo", StringComparison.Ordinal)) throw new Exception("Tibo priority failed");
            tiboEvent = null;
            snapshot.FiveHour.Remaining = 9;
            var afterReset = new UsageSnapshot { RefreshedAt = DateTimeOffset.Now, ResetCreditsAvailable = 1 };
            afterReset.FiveHour.Remaining = 100;
            afterReset.Weekly.Remaining = 100;
            DetectDelivery(afterReset);
            if (FormatRightStatus() != "额度已恢复") throw new Exception("Credit recovery detection failed");
            snapshot = afterReset;
            RebuildFrame(false);
            frame.Save(Path.Combine(output, "reset-restored.png"));
            var parsed = CodexRateLimitReader.ParseForVerification("{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":82,\"windowDurationMins\":10080}},\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":[]}}}");
            if (parsed.ResetCreditsAvailable != 3 || parsed.Weekly.Remaining != 18) throw new Exception("Reset credit parser failed");
            VerifyPriorityAndAlpha(output);
            VerifyCustomScale(output);
            VerifyMenu(output);
            snapshot.FiveHour.Remaining = 76; snapshot.Weekly.Remaining = 42;
            taskState = CodexTaskState.Running; UpdateTrayText();
            if (!trayIcon.Text.Contains("5h 76%") || !trayIcon.Text.Contains("7d 42%") || !trayIcon.Text.Contains("任务运行中"))
                throw new Exception("Tray tooltip failed");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: task states, unchanged quota animation pixels, priority matrix, threshold boundaries, future dates, stale data, recovery once, alpha surface, theme and DPI rendering, app-server parser");
        }

        private void VerifyMenu(string output)
        {
            verifyingMenu = true;
            int original = userScalePercent;
            int cases = 0;
            try
            {
                menu.Show(new Point(80,80));
                Application.DoEvents();
                PrepareMenuForMonitor(true);
                menu.Refresh();
                Application.DoEvents();
                scalePanel.VerifyLayout();
                foreach (int dpi in new[] { 96,120,144,168,192,216,240,288 })
                foreach (int text in new[] { 100,125,150,200 })
                foreach (int theme in new[] { 0,1 })
                foreach (int width in new[] { 800,1920 })
                {
                    background = theme == 0 ? Color.FromArgb(32,32,32) : Color.FromArgb(243,243,243);
                    PrepareMenu(dpi/96f,text/100f,new Rectangle(0,0,width,1080));
                    scalePanel.Slider.Value = 200;
                    scalePanel.VerifyLayout();
                    if (scalePanel.Size != scaleHost.Size) throw new Exception("Host/panel size mismatch");
                    if (menu.Width > width || menu.Height > 1080) throw new Exception("Menu outside work area");
                    scalePanel.Slider.VerifyInput();
                    if (!menu.Visible) throw new Exception("Slider closed menu");
                    scalePanel.Reset.PerformClick();
                    if(userScalePercent != 100 || !menu.Visible) throw new Exception("Reset failed");
                    if (text == 100 && width == 1920 && (dpi == 168 || dpi == 96 || dpi == 192))
                    using (var bitmap = new Bitmap(menu.Width,menu.Height))
                    {
                        menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                        bitmap.Save(Path.Combine(output,"menu-"+dpi+"-"+theme+".png"));
                    }
                    cases++;
                }
                PrepareMenu(1f,1f,new Rectangle(0,0,1920,1080));
                Size initial = scalePanel.Size;
                PrepareMenu(1.75f,1f,new Rectangle(0,0,1920,1080));
                PrepareMenu(1f,1f,new Rectangle(0,0,1920,1080));
                if(scalePanel.Size != initial) throw new Exception("DPI round trip accumulated scaling");
                File.WriteAllText(Path.Combine(output,"menu-result.txt"),
                    "PASS: current-monitor menu opening and repaint; "+cases+" menu layouts; 96/120/144/168/192/216/240/288 DPI; text 100/125/150/200%; dark/light; widths 800/1920; slider mouse/keyboard, reset, host bounds, DPI round trip. Matrix uses simulated layout/rendering, not physical monitor testing.");
            }
            finally
            {
                scalePanel.Slider.Value = original;
                menu.Close();
                verifyingMenu = false;
            }
        }

        private void VerifyCustomScale(string output)
        {
            tiboEvent = null; deliveredUntil = null;
            snapshot.RefreshedAt = DateTimeOffset.Now;
            snapshot.FiveHour.Remaining = 8; snapshot.Weekly.Remaining = 0;
            snapshot.ResetCreditsAvailable = 0;
            snapshot.Weekly.ResetsAt = DateTimeOffset.Now.AddDays(3);
            taskState = CodexTaskState.Running;
            foreach (float dpi in new float[] { 1f, 1.25f, 1.5f, 2f, 3f })
            foreach (int percent in new int[] { 50, 75, 100, 125, 150, 200 })
            {
                displayScale = FitScale(dpi, percent, 1280, (int)(48 * dpi), GetLogicalWidth());
                RebuildFrame(false);
                if (frame.Height > (int)(48 * dpi) - 4 || frame.Width > 1264)
                    throw new Exception("Scale exceeds taskbar bounds");
                using (var before = (Bitmap)frame.Clone())
                {
                    AnimationTick(null, EventArgs.Empty);
                    int changed = 0;
                    for (int y = 0; y < frame.Height; y++)
                    for (int x = 0; x < frame.Width; x++)
                    {
                        if (before.GetPixel(x,y) != frame.GetPixel(x,y))
                        {
                            changed++;
                            if (x >= (int)Math.Ceiling(30 * displayScale)) throw new Exception("Scaled animation changed text");
                        }
                        if (x >= frame.Width - 2 && frame.GetPixel(x,y).A != 0) throw new Exception("Scaled text clipped");
                    }
                    if (changed == 0) throw new Exception("Scaled logo did not animate");
                }
                frame.Save(Path.Combine(output, "custom-" + dpi.ToString(CultureInfo.InvariantCulture) + "-" + percent + ".png"));
            }
            displayScale = 1f;
        }

        private void VerifyPriorityAndAlpha(string output)
        {
            deliveredUntil = null; tiboEvent = null;
            snapshot = new UsageSnapshot { RefreshedAt=DateTimeOffset.Now };
            snapshot.FiveHour.ResetsAt = DateTimeOffset.Now.AddHours(2);
            snapshot.Weekly.ResetsAt = DateTimeOffset.Now.AddDays(3);
            int[,] rows = {{50,80,0,0},{8,80,0,1},{8,15,2,1},{8,1,2,1},{8,0,2,2},{8,0,0,3},{50,0,2,2},{50,0,0,3},{10,80,2,0},{11,80,0,0}};
            for (int i=0; i<rows.GetLength(0); i++)
            {
                snapshot.FiveHour.Remaining=rows[i,0]; snapshot.Weekly.Remaining=rows[i,1]; snapshot.ResetCreditsAvailable=rows[i,2];
                string expected = rows[i,3]==0 ? "" : rows[i,3]==1 ? FormatResetTime(snapshot.FiveHour,false) : rows[i,3]==2 ? "可重置 ×2" : FormatResetTime(snapshot.Weekly,true);
                if (FormatRightStatus()!=expected) throw new Exception("Priority matrix row " + i);
            }
            snapshot.FiveHour.Remaining=9; snapshot.Weekly.Remaining=80;
            snapshot.FiveHour.ResetsAt=null;
            if (FormatRightStatus()!="") throw new Exception("Missing time");
            snapshot.FiveHour.ResetsAt=DateTimeOffset.Now.AddMinutes(-1);
            if (FormatRightStatus()!="") throw new Exception("Past time");
            snapshot.FiveHour.ResetsAt=new DateTimeOffset(DateTime.Today.AddDays(1).AddHours(1));
            if (!FormatRightStatus().Contains("明天")) throw new Exception("Midnight");
            snapshot.Error="offline";
            if (FormatRightStatus()!="") throw new Exception("Stale status");
            snapshot.Error=null; snapshot.Weekly.Remaining=0; snapshot.ResetCreditsAvailable=null;
            if (!FormatRightStatus().StartsWith("7d")) throw new Exception("Unknown credit count");
            tiboEvent=new TiboEvent { TargetAt=DateTimeOffset.Now.AddHours(1) };
            if (!FormatRightStatus().StartsWith("7d")) throw new Exception("Weekly must outrank Tibo");
            snapshot.Weekly.Remaining=80;
            if (!FormatRightStatus().StartsWith("Tibo")) throw new Exception("Future Tibo priority");
            tiboEvent.TargetAt=DateTimeOffset.Now.AddMinutes(-1); snapshot.FiveHour.Remaining=0;
            if (!FormatRightStatus().StartsWith("5h")) throw new Exception("Exhausted five hour priority");
            tiboEvent=null;
            if (IncreasedSubstantially(100,100) || IncreasedSubstantially(100,98)) throw new Exception("False recovery");
            snapshot.FiveHour.Remaining=8; snapshot.Weekly.Remaining=0; snapshot.ResetCreditsAvailable=0;
            for (int theme=0; theme<2; theme++)
            {
                background=theme==0 ? Color.FromArgb(32,32,32) : Color.FromArgb(243,243,243);
                foreground=theme==0 ? Color.White : Color.FromArgb(28,28,28);
                RebuildFrame(false);
                if (frame.GetPixel(frame.Width-1,0).A!=0) throw new Exception("Opaque background");
                for (int x=frame.Width-8; x<frame.Width; x++) for(int y=0;y<frame.Height;y++)
                    if(frame.GetPixel(x,y).A!=0) throw new Exception("Text clipping");
                foreach (float scale in new float[]{1f,1.25f,1.5f,2f})
                using(var bitmap=new Bitmap((int)(frame.Width*scale),(int)(frame.Height*scale)))
                {
                    using(Graphics g=Graphics.FromImage(bitmap)) g.DrawImage(frame,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                    if(bitmap.GetPixel(bitmap.Width-1,0).A!=0) throw new Exception("Scaled alpha");
                    bitmap.Save(Path.Combine(output,"transparent-"+theme+"-"+scale.ToString(CultureInfo.InvariantCulture)+".png"));
                }
            }
            tiboEvent=new TiboEvent { TargetAt=DateTimeOffset.Now.AddMinutes(-1) };
            beforeEventFiveHour=8; beforeEventWeekly=0;
            var recovered=new UsageSnapshot {RefreshedAt=DateTimeOffset.Now};
            recovered.FiveHour.Remaining=100; recovered.Weekly.Remaining=100;
            DetectDelivery(recovered); DateTimeOffset? deadline=deliveredUntil;
            snapshot=recovered; DetectDelivery(recovered);
            if(deliveredUntil!=deadline) throw new Exception("Recovery extended on poll");
            recovered.Weekly.Remaining=0; DetectDelivery(recovered);
            if(deliveredUntil.HasValue) throw new Exception("Recovery hides exhaustion");
        }

        private void SetTaskState(CodexTaskState next)
        {
            if (taskState == next) return;
            taskState = next;
            spinnerAngle = 0;
            if (next == CodexTaskState.Running) animationTimer.Start();
            else animationTimer.Stop();
            UpdateWidth();
            RebuildFrame();
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (taskState != CodexTaskState.Running || frame == null) return;
            spinnerAngle = (spinnerAngle + 6) % 360;
            Rectangle area = new Rectangle(4, 0, StatusExtraWidth + 2, WidgetHeight);
            using (Graphics graphics = Graphics.FromImage(frame))
            {
                graphics.ScaleTransform(displayScale, displayScale);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                using (var brush = new SolidBrush(Color.Transparent)) graphics.FillRectangle(brush, area);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                DrawStatusIcon(graphics);
            }
            PresentFrame();
        }

        private void QueueActivityRefresh()
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    lastCodexActivity = DateTimeOffset.Now;
                    activityRefreshDue = DateTimeOffset.Now.AddSeconds(ActivityDebounceSeconds);
                });
            }
            catch { }
        }

        private void UpdateFreshnessText()
        {
            if (snapshot.RefreshedAt == default(DateTimeOffset))
            {
                freshnessItem.Text = "尚未取得服务器数据";
                return;
            }
            int age = Math.Max(0, (int)(DateTimeOffset.Now - snapshot.RefreshedAt).TotalSeconds);
            string suffix = String.IsNullOrEmpty(snapshot.Error) ? "服务器实值" : "连接失败，保留上次实值";
            freshnessItem.Text = String.Format(CultureInfo.InvariantCulture,
                "{2} · {0:HH:mm:ss}", snapshot.RefreshedAt.LocalDateTime, age, suffix);
        }

        private async void PollTibo()
        {
            tiboPolling = true;
            try
            {
                TiboEvent next = await Task.Run(delegate { return TiboMonitor.FetchExplicitFutureReset(); });
                if (next != null && (tiboEvent == null || next.Id != tiboEvent.Id))
                {
                    beforeEventFiveHour = snapshot.FiveHour.Remaining;
                    beforeEventWeekly = snapshot.Weekly.Remaining;
                }
                tiboEvent = next;
            }
            catch (Exception ex)
            {
                // Keep the last verified event during a temporary source failure.
                AppLog.Write("Tibo refresh failed: " + ex);
            }
            finally
            {
                tiboPolling = false;
                tiboNextPoll = DateTimeOffset.Now.AddMinutes(5);
                UpdateWidth();
                RebuildFrame();
            }
        }

        private void DetectDelivery(UsageSnapshot next)
        {
            bool creditCountDropped = snapshot.ResetCreditsAvailable.HasValue && next.ResetCreditsAvailable.HasValue &&
                next.ResetCreditsAvailable.Value < snapshot.ResetCreditsAvailable.Value;
            bool creditRestored = creditCountDropped &&
                (IncreasedSubstantially(snapshot.FiveHour.Remaining, next.FiveHour.Remaining) ||
                 IncreasedSubstantially(snapshot.Weekly.Remaining, next.Weekly.Remaining));

            bool tiboRestored = false;
            if (tiboEvent != null && DateTimeOffset.Now >= tiboEvent.TargetAt.AddMinutes(-5))
                tiboRestored = IncreasedSubstantially(beforeEventFiveHour, next.FiveHour.Remaining) ||
                               IncreasedSubstantially(beforeEventWeekly, next.Weekly.Remaining);

            if (creditRestored || tiboRestored)
            {
                deliveredUntil = DateTimeOffset.Now.AddMinutes(5);
                if (tiboRestored) { beforeEventFiveHour = null; beforeEventWeekly = null; }
            }
            if (next.FiveHour.Remaining == 0 || next.Weekly.Remaining == 0) deliveredUntil = null;
        }

        private static bool IncreasedSubstantially(int? before, int? after)
        {
            return before.HasValue && after.HasValue && after.Value > before.Value &&
                   (after.Value >= 98 || after.Value - before.Value >= 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (frame == null)
                RebuildFrame(false);
            if (frame != null) PresentFrame();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The complete opaque frame is swapped in OnPaint. Skipping WM_ERASEBKGND
            // prevents a blank taskbar-coloured frame from appearing between updates.
        }

        private void RebuildFrame()
        {
            RebuildFrame(true);
        }

        private void RebuildFrame(bool requestPaint)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            var next = new Bitmap(Math.Max(1, (int)Math.Round(GetLogicalWidth() * displayScale)), Math.Max(1, (int)Math.Round(WidgetHeight * displayScale)),
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(next))
            {
                graphics.ScaleTransform(displayScale, displayScale);
                PaintFrame(graphics);
            }
            Bitmap previous = frame;
            frame = next;
            if (previous != null) previous.Dispose();
            paintedRightStatus = FormatRightStatus();
            UpdateTrayText();
            if (requestPaint) PresentFrame();
        }

        private void UpdateTrayText()
        {
            if (trayIcon == null) return;
            string five = FormatPercent(snapshot.FiveHour.Remaining);
            string weekly = FormatPercent(snapshot.Weekly.Remaining);
            string state = taskState == CodexTaskState.Running ? " · 任务运行中" :
                taskState == CodexTaskState.Completed ? " · 任务已完成" :
                taskState == CodexTaskState.Waiting ? " · 等待输入" :
                taskState == CodexTaskState.Interrupted ? " · 任务已中断" : String.Empty;
            string value = "Codex 额度 · 5h " + five + " · 7d " + weekly + state;
            trayIcon.Text = value.Length > 63 ? value.Substring(0, 63) : value;
        }

        private void PresentFrame()
        {
            if (frame == null || !IsHandleCreated || !Visible) return;
            AlphaWindow.Present(Handle, frame, Left, Top);
        }

        private void PaintFrame(Graphics graphics)
        {
            graphics.Clear(Color.Transparent);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            string five = FormatPercent(snapshot.FiveHour.Remaining);
            string weekly = FormatPercent(snapshot.Weekly.Remaining);
            int x = 8;
            DrawStatusIcon(graphics);
            x += StatusExtraWidth;
            DrawText(graphics, "GPT", mainFont, foreground, ref x);
            x += 10;
            DrawText(graphics, "5h ", smallFont, Muted(), ref x);
            DrawText(graphics, five, mainFont, QuotaColor(snapshot.FiveHour.Remaining), ref x);
            DrawText(graphics, "  |  ", smallFont, Muted(), ref x);
            DrawText(graphics, "7d ", smallFont, Muted(), ref x);
            DrawText(graphics, weekly, mainFont, QuotaColor(snapshot.Weekly.Remaining), ref x);

            string rightStatus = FormatRightStatus();
            if (!String.IsNullOrEmpty(rightStatus))
            {
                DrawText(graphics, "  |  ", smallFont, Muted(), ref x);
                DrawText(graphics, rightStatus, mainFont,
                    rightStatus == "额度已恢复" ? Color.FromArgb(64, 204, 132) : Color.FromArgb(255, 184, 76), ref x);
            }

        }

        private void DrawStatusIcon(Graphics graphics)
        {
            var saved = graphics.Save();
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.TranslateTransform(17, 17);
            if (taskState == CodexTaskState.Running) graphics.RotateTransform(spinnerAngle);
            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
            {
                if (background.R > 128)
                    attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new float[][] {
                        new float[]{0,0,0,0,0}, new float[]{0,0,0,0,0}, new float[]{0,0,0,0,0},
                        new float[]{0,0,0,1,0}, new float[]{0.12f,0.12f,0.12f,0,1}}));
                graphics.DrawImage(knotLogo, new Rectangle(-9, -9, 18, 18), 0, 0, knotLogo.Width, knotLogo.Height, GraphicsUnit.Pixel, attributes);
            }
            graphics.Restore(saved);
            if (taskState == CodexTaskState.Completed)
            {
                saved = graphics.Save();
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                using (var brush = new SolidBrush(Color.Transparent))
                    graphics.FillEllipse(brush, new Rectangle(19, 19, 10, 10));
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                using (var brush = new SolidBrush(Color.FromArgb(45, 190, 105)))
                    graphics.FillEllipse(brush, new Rectangle(20, 20, 8, 8));
                using (var pen = new Pen(Color.White, 1.1f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    graphics.DrawLines(pen, new[] { new Point(22, 24), new Point(23, 25), new Point(26, 22) });
                }
                graphics.Restore(saved);
            }
            else if (taskState == CodexTaskState.Waiting || taskState == CodexTaskState.Interrupted)
            {
                saved = graphics.Save();
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                using (var brush = new SolidBrush(Color.Transparent)) graphics.FillEllipse(brush, 19, 19, 10, 10);
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                if (taskState == CodexTaskState.Waiting)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(232, 173, 69))) graphics.FillEllipse(brush, 21, 21, 6, 6);
                }
                else using (var pen = new Pen(Muted(), 1.4f))
                { graphics.DrawLine(pen, 21, 21, 27, 27); graphics.DrawLine(pen, 21, 27, 27, 21); }
                graphics.Restore(saved);
            }
        }

        private void DrawText(Graphics g, string value, Font font, Color color, ref int x)
        {
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (var brush = new SolidBrush(color))
            {
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                SizeF size = g.MeasureString(value, font, Int32.MaxValue, format);
                g.DrawString(value, font, brush, new PointF(x, (WidgetHeight - size.Height) / 2f), format);
                x += (int)Math.Ceiling(size.Width);
            }
        }

        private string FormatRightStatus()
        {
            if (deliveredUntil.HasValue && deliveredUntil.Value > DateTimeOffset.Now) return "额度已恢复";
            bool fresh = !IsQuotaStale();
            if (fresh && snapshot.Weekly.Remaining == 0)
            {
                if (snapshot.ResetCreditsAvailable > 0)
                    return "可重置 ×" + snapshot.ResetCreditsAvailable.Value.ToString(CultureInfo.InvariantCulture);
                return FormatResetTime(snapshot.Weekly, true);
            }
            if (tiboEvent != null)
            {
                TimeSpan left = tiboEvent.TargetAt - DateTimeOffset.Now;
                if (left <= TimeSpan.Zero)
                {
                    string urgent = fresh && snapshot.FiveHour.Remaining == 0 ? FormatResetTime(snapshot.FiveHour, false) : String.Empty;
                    return String.IsNullOrEmpty(urgent) ? "Tibo 待确认" : urgent;
                }
                if (left.TotalDays >= 1)
                    return String.Format(CultureInfo.InvariantCulture, "Tibo {0}天 {1:00}:{2:00}:{3:00}",
                        (int)left.TotalDays, left.Hours, left.Minutes, left.Seconds);
                return String.Format(CultureInfo.InvariantCulture, "Tibo {0:00}:{1:00}:{2:00}",
                    (int)left.TotalHours, left.Minutes, left.Seconds);
            }
            if (fresh && snapshot.FiveHour.Remaining < 10)
                return FormatResetTime(snapshot.FiveHour, false);
            return String.Empty;
        }

        private static string FormatResetTime(LimitReading reading, bool weekly)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (!reading.ResetsAt.HasValue || reading.ResetsAt.Value <= now) return String.Empty;
            DateTime reset = reading.ResetsAt.Value.LocalDateTime;
            string prefix = weekly ? "7d " : "5h ";
            string time = reset.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (!weekly && reset.Date == now.LocalDateTime.Date) return prefix + time + "恢复";
            if (!weekly && reset.Date == now.LocalDateTime.Date.AddDays(1)) return prefix + "明天" + time + "恢复";
            return prefix + reset.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + "恢复";
        }

        private int GetLogicalWidth()
        {
            string status = FormatRightStatus();
            if (String.IsNullOrEmpty(status)) return CompactWidth + StatusExtraWidth;
            return (status.StartsWith("Tibo", StringComparison.Ordinal) || status.EndsWith("恢复", StringComparison.Ordinal) ? CountdownWidth : StatusWidth) + StatusExtraWidth;
        }

        private static string FormatPercent(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) + "%" : "--";
        }

        private Color QuotaColor(int? remaining)
        {
            if (IsQuotaStale()) return Muted();
            if (!remaining.HasValue) return Muted();
            if (remaining.Value < 20) return Color.FromArgb(240, 86, 86);
            if (remaining.Value < 50) return Color.FromArgb(239, 183, 66);
            return Color.FromArgb(64, 204, 132);
        }

        private Color Muted()
        {
            return background.R > 128 ? Color.FromArgb(105, 105, 105) : Color.FromArgb(165, 165, 165);
        }

        private void ApplyTheme()
        {
            bool light = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    light = value != null && Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
                }
            }
            catch { }
            background = light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
            foreground = light ? Color.FromArgb(28, 28, 28) : Color.FromArgb(245, 245, 245);
        }

        private void UpdateWidth()
        {
            int wanted = GetLogicalWidth();
            wanted = (int)Math.Round(wanted * displayScale);
            if (Width != wanted)
            {
                Width = wanted;
                AttachAndPosition();
            }
        }

        private void AttachAndPosition()
        {
            Screen chosen = Screen.PrimaryScreen;
            foreach (Screen screen in Screen.AllScreens) if (screen.DeviceName == monitorDevice) chosen = screen;
            IntPtr current = NativeMethods.FindWindow("Shell_TrayWnd", null);
            NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                var name = new System.Text.StringBuilder(128);
                NativeMethods.GetClassName(hwnd, name, name.Capacity);
                if ((name.ToString() == "Shell_TrayWnd" || name.ToString() == "Shell_SecondaryTrayWnd") &&
                    Screen.FromHandle(hwnd).DeviceName == chosen.DeviceName) current = hwnd;
                return true;
            }, IntPtr.Zero);
            if (current == IntPtr.Zero) return;
            chosen = Screen.FromHandle(current);
            if (taskbarHandle != current || NativeMethods.GetWindow(Handle, 4) != current)
            {
                taskbarHandle = current;
                int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
                style = (style | NativeMethods.WS_POPUP) & ~NativeMethods.WS_CHILD;
                NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, style);
                // Owned top-level windows stay above their owner when it is activated.
                // This is ownership, not SetParent/WS_CHILD: rendering and input remain
                // in this process, and Explorer activation cannot cover the widget.
                NativeMethods.SetOwner(Handle, current);
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            }
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(current, out rect)) return;
            Rectangle barBounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            Rectangle visibleBar = Rectangle.Intersect(chosen.Bounds, barBounds);
            bool barVisible = NativeMethods.IsWindowVisible(current) && visibleBar.Height > 4 && visibleBar.Width > 4;
            if (NativeMethods.IsWindowVisible(Handle) != barVisible) { if (barVisible) NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE); else NativeMethods.ShowWindow(Handle, 0); }
            if (!barVisible) { logoInput.Hide(); return; }
            float scale = 1f;
            try { scale = Math.Max(1f, NativeMethods.GetDpiForWindow(current) / 96f); } catch { }
            scale = FitScale(scale, userScalePercent, barBounds.Width, barBounds.Height, GetLogicalWidth());
            if (displayScale != scale)
            {
                displayScale = scale;
                Size = new Size((int)Math.Round(GetLogicalWidth() * scale), (int)Math.Round(WidgetHeight * scale));
                RebuildFrame();
            }
            int taskbarHeight = rect.Bottom - rect.Top;
            int y = Math.Max(0, (taskbarHeight - Height) / 2);
            int maxX = Math.Max(DefaultLeftOffset, rect.Right - rect.Left - Width - 8);
            leftOffset = Math.Max(0, Math.Min(leftOffset, maxX));
            int wantedX = rect.Left + leftOffset;
            int wantedY = rect.Top + y;
            NativeMethods.RECT currentRect;
            bool alreadyPositioned = NativeMethods.GetWindowRect(Handle, out currentRect) &&
                currentRect.Left == wantedX && currentRect.Top == wantedY &&
                currentRect.Right - currentRect.Left == Width &&
                currentRect.Bottom - currentRect.Top == Height;
            if (!alreadyPositioned)
            {
                NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
                    wantedX, wantedY, Width, Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }
            logoInput.Position(Handle, wantedX, wantedY, (int)Math.Round(30 * displayScale), Height);
        }

        private static float FitScale(float dpiScale, int percent, int barWidth, int barHeight, int logicalWidth)
        {
            float requested = dpiScale * Math.Max(50, Math.Min(200, percent)) / 100f;
            return Math.Max(0.01f, Math.Min(requested, Math.Min(Math.Max(1, barHeight - 4) / (float)WidgetHeight,
                Math.Max(1, barWidth - 16) / (float)logicalWidth)));
        }

        private Control BuildScaleControl()
        {
            scalePanel = new ScalePanel(userScalePercent);
            scalePanel.Slider.ValueChanged += delegate {
                userScalePercent = scalePanel.Slider.Value;
                if (!verifyingMenu) AttachAndPosition();
            };
            return scalePanel;
        }

        private void PrepareMenu(float dpiScale, float textScale, Rectangle workArea)
        {
            // Pixel fonts make this pass independent of the monitor that created the
            // menu. Measure and assign every size from a fresh baseline on each open.
            menu.SuspendLayout();
            Font previous = menuFont;
            menuFont = new Font("Segoe UI", 10f * 96f / 72f * dpiScale * textScale, FontStyle.Regular, GraphicsUnit.Pixel);
            // Keep complete action labels on narrow work areas with large text.
            int longest = 1;
            foreach(ToolStripItem item in menu.Items)
                if(item is ToolStripMenuItem) longest=Math.Max(longest,TextRenderer.MeasureText(item.Text,menuFont).Width);
            int available = Math.Max(100,workArea.Width-(int)Math.Ceiling(80*dpiScale));
            if(longest>available)
            {
                Font fitted = new Font(menuFont.FontFamily,menuFont.Size*available/longest,FontStyle.Regular,GraphicsUnit.Pixel);
                menuFont.Dispose(); menuFont=fitted;
            }
            try
            {
                menu.Font = menuFont;
                int pad = (int)Math.Ceiling(8 * dpiScale);
                menu.Padding = new Padding(pad);
                menu.MaximumSize = new Size(Math.Max(1,workArea.Width-8),Math.Max(1,workArea.Height-8));
                int width = Math.Min((int)Math.Ceiling(310*dpiScale),Math.Max(1,workArea.Width-pad*6-40));
                scalePanel.Font = menuFont;
                scalePanel.Arrange(dpiScale,width);
                scaleHost.AutoSize = false;
                scaleHost.Size = scalePanel.Size;
                foreach (ToolStripItem item in menu.Items)
                {
                    if (item is ToolStripMenuItem)
                        item.Padding = new Padding((int)Math.Ceiling(10*dpiScale),(int)Math.Ceiling(5*dpiScale),
                            (int)Math.Ceiling(10*dpiScale),(int)Math.Ceiling(5*dpiScale));
                    var entry = item as ToolStripMenuItem;
                    if (entry != null && entry.HasDropDownItems) entry.DropDown.Font = menuFont;
                }
                StyleMenu(menu);
            }
            finally { menu.ResumeLayout(true); if(previous != null) previous.Dispose(); }
        }

        private void PrepareMenuForMonitor(bool opened)
        {
            IntPtr source = opened ? menu.Handle : logoInput.Handle;
            float dpi = 1f;
            try { dpi = Math.Max(1f,NativeMethods.GetDpiForWindow(source)/96f); } catch { }
            float text = 1f;
            try { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility"))
                if(key!=null) text=Math.Max(1f,Math.Min(2.25f,Convert.ToSingle(key.GetValue("TextScaleFactor",100))/100f)); } catch { }
            Rectangle work = Screen.FromHandle(source).WorkingArea;
            PrepareMenu(dpi,text,work);
            if(opened)
            {
                menu.Location = new Point(Math.Max(work.Left,Math.Min(menu.Left,work.Right-menu.Width)),
                    Math.Max(work.Top,Math.Min(menu.Top,work.Bottom-menu.Height)));
            }
        }

        private void StyleMenu(ToolStrip strip)
        {
            bool light = background.R > 128;
            strip.BackColor = light ? Color.FromArgb(250,250,250) : Color.FromArgb(36,36,36);
            strip.ForeColor = light ? Color.FromArgb(32,32,32) : Color.FromArgb(240,240,240);
            strip.Renderer = new WidgetMenuRenderer(light);
            foreach (ToolStripItem item in strip.Items)
            {
                item.ForeColor = strip.ForeColor;
                var host = item as ToolStripControlHost;
                if (host != null)
                {
                    host.Control.BackColor = strip.BackColor;
                    host.Control.ForeColor = strip.ForeColor;
                    foreach (Control child in host.Control.Controls)
                    {
                        child.BackColor = strip.BackColor;
                        child.ForeColor = strip.ForeColor;
                    }
                }
                var entry = item as ToolStripMenuItem;
                if (entry != null && entry.HasDropDownItems) StyleMenu(entry.DropDown);
            }
        }

        private void OnWidgetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragStart = logoInput.PointToScreen(e.Location);
            dragOffsetStart = leftOffset;
            logoInput.Capture = true;
        }

        private void OnWidgetMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            leftOffset = Math.Max(0, dragOffsetStart + logoInput.PointToScreen(e.Location).X - dragStart.X);
            AttachAndPosition();
        }

        private void OnWidgetMouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            logoInput.Capture = false;
            SaveLeftOffset();
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        private int ReadLeftOffset()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    object value = key == null ? null : key.GetValue("LeftOffset");
                    return value == null ? DefaultLeftOffset : Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
            }
            catch { return DefaultLeftOffset; }
        }

        private void SaveLeftOffset()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("LeftOffset", leftOffset, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && key.GetValue(StartupName) != null;
            }
            catch { return false; }
        }

        private static void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (enabled) key.SetValue(StartupName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(StartupName, false);
                }
            }
            catch { }
        }
    }

    internal static class CodexRateLimitReader
    {
        public static UsageSnapshot Fetch(bool needResetCredits)
        {
            // Prefer the dedicated usage endpoint so a CLI process is not started on every poll.
            try
            {
                UsageSnapshot direct = FetchThroughCurlFallback();
                if (needResetCredits || (direct.Weekly.Remaining.HasValue && direct.Weekly.Remaining.Value < 20))
                    return FetchThroughAppServer();
                return direct;
            }
            catch (Exception directError)
            {
                AppLog.Write("Direct quota read failed; trying Codex app-server: " + directError.Message);
                return FetchThroughAppServer();
            }
        }

        private static UsageSnapshot FetchThroughAppServer()
        {
            var result = new UsageSnapshot { RefreshedAt = DateTimeOffset.Now };
            string comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var start = new ProcessStartInfo(comspec,
                "/d /s /c \"codex.cmd app-server --stdio\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = new Process { StartInfo = start })
            {
                if (!process.Start()) throw new InvalidOperationException("无法启动 Codex");
                try
                {
                    process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"klee-codex-quota-widget\",\"version\":\"1.2.0\"},\"capabilities\":{\"experimentalApi\":true}}}");
                    process.StandardInput.Flush();
                    string initialized = ReadResponse(process, 1, 10000);
                    ThrowProtocolError(initialized);
                    process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                    process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":null}");
                    process.StandardInput.Flush();
                    string response = ReadResponse(process, 2, 10000);
                    ThrowProtocolError(response);
                    Parse(response, result);
                    return result;
                }
                finally
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch { }
                }
            }
        }

        private static UsageSnapshot FetchThroughCurlFallback()
        {
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(codexHome))
                codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            string authPath = Path.Combine(codexHome, "auth.json");
            if (!File.Exists(authPath)) throw new FileNotFoundException("找不到 Codex 登录信息", authPath);
            var auth = DeserializeObject(File.ReadAllText(authPath));
            object tokensObject;
            if (!auth.TryGetValue("tokens", out tokensObject) || tokensObject == null)
                throw new InvalidDataException("Codex 登录信息中没有 ChatGPT 令牌");
            var tokens = AsMap(tokensObject);
            object accessObject;
            if (!tokens.TryGetValue("access_token", out accessObject) || accessObject == null)
                throw new InvalidDataException("Codex 登录信息中没有访问令牌");
            string accessToken = Convert.ToString(accessObject, CultureInfo.InvariantCulture);
            object accountObject;
            string accountId = tokens.TryGetValue("account_id", out accountObject) && accountObject != null
                ? Convert.ToString(accountObject, CultureInfo.InvariantCulture) : null;

            var start = new ProcessStartInfo("curl.exe", "--config -")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = new Process { StartInfo = start })
            {
                if (!process.Start()) throw new InvalidOperationException("无法启动系统 curl.exe");
                string proxy = ReadSystemProxy();
                AppLog.Write("Curl fallback network route: " + (String.IsNullOrEmpty(proxy) ? "direct" : proxy));
                process.StandardInput.WriteLine("silent");
                process.StandardInput.WriteLine("show-error");
                process.StandardInput.WriteLine("http1.1");
                process.StandardInput.WriteLine("connect-timeout = 5");
                process.StandardInput.WriteLine("max-time = 12");
                process.StandardInput.WriteLine("header = \"Authorization: Bearer " + accessToken + "\"");
                process.StandardInput.WriteLine("header = \"User-Agent: codex-cli\"");
                if (!String.IsNullOrEmpty(accountId))
                    process.StandardInput.WriteLine("header = \"ChatGPT-Account-Id: " + accountId + "\"");
                if (!String.IsNullOrEmpty(proxy)) process.StandardInput.WriteLine("proxy = \"" + proxy + "\"");
                process.StandardInput.WriteLine("url = \"https://chatgpt.com/backend-api/wham/usage\"");
                process.StandardInput.Close();
                string json = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(15000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("备用额度请求超时");
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("备用额度请求失败：" + error.Trim());
                return ParseWhamResponse(json);
            }
        }

        private static string ReadSystemProxy()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (key == null || Convert.ToInt32(key.GetValue("ProxyEnable", 0), CultureInfo.InvariantCulture) == 0)
                        return null;
                    string proxy = Convert.ToString(key.GetValue("ProxyServer"), CultureInfo.InvariantCulture);
                    if (String.IsNullOrWhiteSpace(proxy)) return null;
                    if (proxy.IndexOf('=') >= 0)
                    {
                        foreach (string part in proxy.Split(';'))
                            if (part.StartsWith("https=", StringComparison.OrdinalIgnoreCase)) proxy = part.Substring(6);
                    }
                    if (!proxy.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !proxy.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) proxy = "http://" + proxy;
                    return proxy;
                }
            }
            catch { return null; }
        }

        private static UsageSnapshot ParseWhamResponse(string json)
        {
            var root = DeserializeObject(json);
            object rateObject;
            if (!root.TryGetValue("rate_limit", out rateObject) || rateObject == null)
                throw new InvalidDataException("备用接口没有返回额度");
            var rate = AsMap(rateObject);
            var result = new UsageSnapshot { RefreshedAt = DateTimeOffset.Now };
            ReadWhamWindow(rate, "primary_window", result);
            ReadWhamWindow(rate, "secondary_window", result);
            return result;
        }

        private static void ReadWhamWindow(Dictionary<string, object> rate, string key, UsageSnapshot target)
        {
            object value;
            if (!rate.TryGetValue(key, out value) || value == null) return;
            var window = AsMap(value);
            object durationValue;
            object usedValue;
            if (!window.TryGetValue("limit_window_seconds", out durationValue) ||
                !window.TryGetValue("used_percent", out usedValue)) return;
            long seconds = Convert.ToInt64(durationValue, CultureInfo.InvariantCulture);
            int used = Convert.ToInt32(usedValue, CultureInfo.InvariantCulture);
            var reading = new LimitReading { Remaining = Math.Max(0, Math.Min(100, 100 - used)) };
            object resetValue;
            if (window.TryGetValue("reset_at", out resetValue) && resetValue != null)
                reading.ResetsAt = UnixTime(Convert.ToInt64(resetValue, CultureInfo.InvariantCulture)).ToLocalTime();
            if (seconds == 18000) target.FiveHour = reading;
            if (seconds == 604800) target.Weekly = reading;
        }

        private static string ReadResponse(Process process, int wantedId, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                Task<string> read = process.StandardOutput.ReadLineAsync();
                int wait = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                if (!read.Wait(wait)) throw new TimeoutException("读取 Codex 额度超时");
                string line = read.Result;
                if (line == null) throw new IOException("Codex 在返回额度前已退出");
                var root = DeserializeObject(line);
                object id;
                if (root.TryGetValue("id", out id) && Convert.ToInt32(id, CultureInfo.InvariantCulture) == wantedId)
                    return line;
            }
            throw new TimeoutException("读取 Codex 额度超时");
        }

        private static void ThrowProtocolError(string response)
        {
            var root = DeserializeObject(response);
            object error;
            if (!root.TryGetValue("error", out error)) return;
            var errorMap = error as Dictionary<string, object>;
            object message;
            string detail = errorMap != null && errorMap.TryGetValue("message", out message)
                ? Convert.ToString(message, CultureInfo.InvariantCulture) : "未知错误";
            throw new InvalidOperationException("Codex 无法返回额度：" + detail);
        }

        private static void Parse(string response, UsageSnapshot target)
        {
            var root = DeserializeObject(response);
            var result = AsMap(root["result"]);
            Dictionary<string, object> bucket = null;
            object bucketsObject;
            if (result.TryGetValue("rateLimitsByLimitId", out bucketsObject) && bucketsObject != null)
            {
                var buckets = AsMap(bucketsObject);
                object codex;
                if (buckets.TryGetValue("codex", out codex) && codex != null) bucket = AsMap(codex);
            }
            if (bucket == null)
            {
                object legacy;
                if (!result.TryGetValue("rateLimits", out legacy) || legacy == null)
                    throw new InvalidOperationException("Codex 没有返回通用额度桶");
                bucket = AsMap(legacy);
            }
            ReadWindow(bucket, "primary", target);
            ReadWindow(bucket, "secondary", target);
            object resetCreditsObject;
            if (result.TryGetValue("rateLimitResetCredits", out resetCreditsObject) && resetCreditsObject != null)
            {
                var resetCredits = AsMap(resetCreditsObject);
                object availableCount;
                if (resetCredits.TryGetValue("availableCount", out availableCount) && availableCount != null)
                    target.ResetCreditsAvailable = Math.Max(0, Convert.ToInt32(availableCount, CultureInfo.InvariantCulture));
            }
            target.Error = null;
        }

        internal static UsageSnapshot ParseForVerification(string response)
        {
            var target = new UsageSnapshot { RefreshedAt = DateTimeOffset.Now };
            Parse(response, target);
            return target;
        }

        private static void ReadWindow(Dictionary<string, object> bucket, string key, UsageSnapshot target)
        {
            object value;
            if (!bucket.TryGetValue(key, out value) || value == null) return;
            var window = AsMap(value);
            object durationValue;
            object usedValue;
            if (!window.TryGetValue("windowDurationMins", out durationValue) || durationValue == null ||
                !window.TryGetValue("usedPercent", out usedValue)) return;
            long duration = Convert.ToInt64(durationValue, CultureInfo.InvariantCulture);
            int used = Convert.ToInt32(usedValue, CultureInfo.InvariantCulture);
            var reading = new LimitReading { Remaining = Math.Max(0, Math.Min(100, 100 - used)) };
            object resetValue;
            if (window.TryGetValue("resetsAt", out resetValue) && resetValue != null)
            {
                long seconds = Convert.ToInt64(resetValue, CultureInfo.InvariantCulture);
                reading.ResetsAt = UnixTime(seconds).ToLocalTime();
            }
            if (duration == 300) target.FiveHour = reading;
            if (duration == 10080) target.Weekly = reading;
        }

        private static DateTimeOffset UnixTime(long seconds)
        {
            return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);
        }

        private static Dictionary<string, object> DeserializeObject(string json)
        {
            var value = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (value == null) throw new InvalidDataException("Codex 返回了无效 JSON");
            return value;
        }

        private static Dictionary<string, object> AsMap(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map == null) throw new InvalidDataException("Codex 返回结构已变化");
            return map;
        }
    }

    internal static class TiboMonitor
    {
        private const string MonitorUrl = "https://codexreset.org/";

        public static TiboEvent FetchExplicitFutureReset()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string html;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "KleeCodexQuotaWidget/0.1 (+local desktop monitor)";
                client.Encoding = System.Text.Encoding.UTF8;
                html = client.DownloadString(MonitorUrl);
            }

            var events = new List<TiboEvent>();
            int cursor = 0;
            const string marker = "title:\"reset intent\"";
            while (true)
            {
                int markerAt = html.IndexOf(marker, cursor, StringComparison.OrdinalIgnoreCase);
                if (markerAt < 0) break;
                int start = html.LastIndexOf("{id:\"", markerAt, StringComparison.Ordinal);
                int end = html.IndexOf("sourceUrl:\"", markerAt, StringComparison.Ordinal);
                if (start >= 0 && end > markerAt && end - start < 12000)
                {
                    int close = html.IndexOf("\"}", end + 11, StringComparison.Ordinal);
                    if (close > end)
                    {
                        string record = html.Substring(start, close + 2 - start);
                        TiboEvent parsed = ParseRecord(record);
                        if (parsed != null) events.Add(parsed);
                    }
                }
                cursor = markerAt + marker.Length;
            }

            TiboEvent best = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (TiboEvent item in events)
            {
                // Keep an announced event visible for up to two hours after its target while delivery propagates.
                if (item.TargetAt < now.AddHours(-2) || item.PostedAt > now.AddMinutes(5)) continue;
                if (best == null || item.PostedAt > best.PostedAt) best = item;
            }
            return best;
        }

        private static TiboEvent ParseRecord(string record)
        {
            if (record.IndexOf("author:\"Tibo\"", StringComparison.OrdinalIgnoreCase) < 0) return null;
            string id = Capture(record, "id:\\\"(?<v>\\d+)\\\"");
            string created = Capture(record, "createdAt:\\\"(?<v>[^\\\"]+)\\\"");
            string rawSummary = Capture(record, "summary:\\\"(?<v>(?:\\\\.|[^\\\"\\\\])*)\\\"");
            string url = Capture(record, "sourceUrl:\\\"(?<v>[^\\\"]+)\\\"");
            if (id == null || created == null || rawSummary == null || url == null) return null;
            DateTimeOffset posted;
            if (!DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out posted)) return null;
            string text = DecodeJsString(rawSummary);
            DateTimeOffset target;
            if (!TryParseExplicitTarget(text, posted, out target)) return null;
            return new TiboEvent { Id = id, Text = text, SourceUrl = url, PostedAt = posted, TargetAt = target };
        }

        private static bool TryParseExplicitTarget(string text, DateTimeOffset postedUtc, out DateTimeOffset targetUtc)
        {
            targetUtc = default(DateTimeOffset);
            Match utc = Regex.Match(text,
                @"(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<ampm>am|pm)?\s*UTC",
                RegexOptions.IgnoreCase);
            if (utc.Success)
            {
                DateTime date = postedUtc.UtcDateTime.Date;
                if (Regex.IsMatch(text, @"\btomorrow\b", RegexOptions.IgnoreCase)) date = date.AddDays(1);
                int hour = ParseHour(utc.Groups["hour"].Value, utc.Groups["ampm"].Value);
                if (hour < 0 || hour > 23) return false;
                int minute = String.IsNullOrEmpty(utc.Groups["minute"].Value) ? 0 : Int32.Parse(utc.Groups["minute"].Value, CultureInfo.InvariantCulture);
                targetUtc = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.Zero);
                return targetUtc > postedUtc;
            }

            Match pacific = Regex.Match(text,
                @"(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<ampm>am|pm)\s*(?:PST|PDT|PT)",
                RegexOptions.IgnoreCase);
            if (!pacific.Success) return false;
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
            catch { return false; }
            DateTime pacificPost = TimeZoneInfo.ConvertTime(postedUtc, zone).DateTime;
            DateTime dateAtZone = pacificPost.Date;
            if (Regex.IsMatch(text, @"\btomorrow\b", RegexOptions.IgnoreCase)) dateAtZone = dateAtZone.AddDays(1);
            int h = ParseHour(pacific.Groups["hour"].Value, pacific.Groups["ampm"].Value);
            if (h < 0 || h > 23) return false;
            int m = String.IsNullOrEmpty(pacific.Groups["minute"].Value) ? 0 : Int32.Parse(pacific.Groups["minute"].Value, CultureInfo.InvariantCulture);
            DateTime localTarget = new DateTime(dateAtZone.Year, dateAtZone.Month, dateAtZone.Day, h, m, 0, DateTimeKind.Unspecified);
            targetUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTarget, zone), TimeSpan.Zero);
            return targetUtc > postedUtc;
        }

        private static int ParseHour(string hourText, string ampm)
        {
            int hour = Int32.Parse(hourText, CultureInfo.InvariantCulture);
            if (String.IsNullOrEmpty(ampm)) return hour;
            if (hour < 1 || hour > 12) return -1;
            if (hour == 12) hour = 0;
            if (ampm.Equals("pm", StringComparison.OrdinalIgnoreCase)) hour += 12;
            return hour;
        }

        private static string Capture(string input, string pattern)
        {
            Match match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups["v"].Value : null;
        }

        private static string DecodeJsString(string raw)
        {
            try { return new JavaScriptSerializer().Deserialize<string>("\"" + raw + "\""); }
            catch { return raw.Replace("\\n", " ").Replace("\\\"", "\""); }
        }
    }

    internal sealed class LogoInputWindow : Form
    {
        public LogoInputWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08080080; return cp; }
        }
        public void Position(IntPtr owner, int x, int y, int width, int height)
        {
            bool changed = !Visible || Left != x || Top != y || Width != width || Height != height;
            if (!changed) return;
            NativeMethods.SetOwner(Handle, owner);
            using (var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(Color.FromArgb(1, 0, 0, 0));
                AlphaWindow.Present(Handle, bitmap, x, y);
            }
            if (!Visible) Show();
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, x, y, width, height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    internal static class AlphaWindow
    {
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; public Point(int x, int y) { X=x; Y=y; } }
        [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; public Size(int w, int h) { Width=w; Height=h; } }
        [StructLayout(LayoutKind.Sequential, Pack=1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Point location, ref Size size, IntPtr src, ref Point origin, int key, ref Blend blend, int flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        public static void Present(IntPtr hwnd, Bitmap bitmap, int x, int y)
        {
            IntPtr screen = GetDC(IntPtr.Zero), dc = IntPtr.Zero, handle = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                dc = CreateCompatibleDC(screen);
                handle = bitmap.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(dc, handle);
                var location = new Point(x, y); var origin = new Point(0, 0);
                var size = new Size(bitmap.Width, bitmap.Height);
                var blend = new Blend { Alpha=255, Format=1 };
                if (!UpdateLayeredWindow(hwnd, screen, ref location, ref size, dc, ref origin, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(dc, old);
                if (handle != IntPtr.Zero) DeleteObject(handle);
                if (dc != IntPtr.Zero) DeleteDC(dc);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
        public static void SetOwner(IntPtr hwnd, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, -8, owner);
            else SetWindowLong(hwnd, -8, owner.ToInt32());
        }
        public delegate bool EnumWindowProc(IntPtr hwnd, IntPtr data);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int capacity);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        public const int GWL_STYLE = -16;
        public const int WS_CHILD = 0x40000000;
        public const int WS_CLIPSIBLINGS = 0x04000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int SW_SHOWNOACTIVATE = 4;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")]
        public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr child);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")]
        public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    }

    internal static class AppLog
    {
        public static void Write(string message)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KleeCodexQuotaWidget");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "widget.log"),
                    DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }

    internal static class UsageCache
    {
        private static string CachePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KleeCodexQuotaWidget", "usage-cache.json");
            }
        }

        public static UsageSnapshot Load()
        {
            var empty = new UsageSnapshot();
            try
            {
                if (!File.Exists(CachePath)) return empty;
                var serializer = new JavaScriptSerializer();
                var data = serializer.DeserializeObject(File.ReadAllText(CachePath)) as Dictionary<string, object>;
                if (data == null) return empty;
                object refreshedValue;
                DateTimeOffset refreshed;
                if (!data.TryGetValue("refreshedAt", out refreshedValue) ||
                    !DateTimeOffset.TryParse(Convert.ToString(refreshedValue, CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out refreshed) ||
                    DateTimeOffset.Now - refreshed > TimeSpan.FromDays(2)) return empty;
                var result = new UsageSnapshot { RefreshedAt = refreshed };
                object five;
                object weekly;
                object resetCredits;
                if (data.TryGetValue("fiveHour", out five) && five != null)
                    result.FiveHour.Remaining = Convert.ToInt32(five, CultureInfo.InvariantCulture);
                if (data.TryGetValue("weekly", out weekly) && weekly != null)
                    result.Weekly.Remaining = Convert.ToInt32(weekly, CultureInfo.InvariantCulture);
                if (data.TryGetValue("resetCreditsAvailable", out resetCredits) && resetCredits != null)
                    result.ResetCreditsAvailable = Math.Max(0, Convert.ToInt32(resetCredits, CultureInfo.InvariantCulture));
                result.FiveHour.ResetsAt = ReadCachedTime(data, "fiveHourResetsAt");
                result.Weekly.ResetsAt = ReadCachedTime(data, "weeklyResetsAt");
                return result;
            }
            catch { return empty; }
        }

        private static DateTimeOffset? ReadCachedTime(Dictionary<string, object> data, string name)
        {
            object value; DateTimeOffset parsed;
            return data.TryGetValue(name, out value) && value != null &&
                DateTimeOffset.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out parsed) ? (DateTimeOffset?)parsed : null;
        }

        public static void Save(UsageSnapshot snapshot)
        {
            try
            {
                string directory = Path.GetDirectoryName(CachePath);
                Directory.CreateDirectory(directory);
                var data = new Dictionary<string, object>();
                data["fiveHour"] = snapshot.FiveHour.Remaining;
                data["weekly"] = snapshot.Weekly.Remaining;
                data["resetCreditsAvailable"] = snapshot.ResetCreditsAvailable;
                data["fiveHourResetsAt"] = snapshot.FiveHour.ResetsAt.HasValue ? snapshot.FiveHour.ResetsAt.Value.ToString("o", CultureInfo.InvariantCulture) : null;
                data["weeklyResetsAt"] = snapshot.Weekly.ResetsAt.HasValue ? snapshot.Weekly.ResetsAt.Value.ToString("o", CultureInfo.InvariantCulture) : null;
                data["refreshedAt"] = snapshot.RefreshedAt.ToString("o", CultureInfo.InvariantCulture);
                File.WriteAllText(CachePath, new JavaScriptSerializer().Serialize(data));
            }
            catch { }
        }
    }

    internal static class ColorExtensions
    {
        public static Color Blend(this Color source, Color target, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                (int)(source.R * (1 - amount) + target.R * amount),
                (int)(source.G * (1 - amount) + target.G * amount),
                (int)(source.B * (1 - amount) + target.B * amount));
        }
    }
}
