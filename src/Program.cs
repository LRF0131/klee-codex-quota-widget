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
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]

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
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new QuotaForm()) form.VerifyStatus(args[1]);
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
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem freshnessItem;
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
        private Color background;
        private Color foreground;

        public QuotaForm()
        {
            leftOffset = ReadLeftOffset();
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

            menu = new ContextMenuStrip();
            menu.Items.Add("立即刷新", null, delegate { ForceRefresh(); });
            freshnessItem = new ToolStripMenuItem("尚未取得服务器数据");
            freshnessItem.Enabled = false;
            menu.Items.Add(freshnessItem);
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
            menu.Opening += delegate { UpdateFreshnessText(); };
            ContextMenuStrip = menu;

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 250;
            uiTimer.Tick += UiTick;

            animationTimer = new System.Windows.Forms.Timer();
            animationTimer.Interval = 33;
            animationTimer.Tick += AnimationTick;

            MouseDown += OnWidgetMouseDown;
            MouseMove += OnWidgetMouseMove;
            MouseUp += OnWidgetMouseUp;
            MouseDoubleClick += delegate { ForceRefresh(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyTheme();
            AttachAndPosition();
            RebuildFrame();
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
            if (frame != null) frame.Dispose();
            menu.Dispose();
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
            if (deliveredUntil.HasValue && now > deliveredUntil.Value)
            {
                deliveredUntil = null;
                tiboEvent = null;
            }
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
            snapshot.Weekly.Remaining = 19;
            snapshot.ResetCreditsAvailable = 2;
            deliveredUntil = null;
            tiboEvent = null;
            if (FormatRightStatus() != "可重置 ×2") throw new Exception("Reset opportunity label failed");
            RebuildFrame(false);
            frame.Save(Path.Combine(output, "reset-available.png"));
            snapshot.Weekly.Remaining = 20;
            if (FormatRightStatus() != String.Empty) throw new Exception("Reset threshold boundary failed");
            snapshot.Weekly.Remaining = 19;
            snapshot.ResetCreditsAvailable = 0;
            if (FormatRightStatus() != String.Empty) throw new Exception("Zero reset credit should stay hidden");
            snapshot.ResetCreditsAvailable = 2;
            tiboEvent = new TiboEvent { TargetAt = DateTimeOffset.Now.AddHours(1) };
            if (!FormatRightStatus().StartsWith("Tibo", StringComparison.Ordinal)) throw new Exception("Tibo priority failed");
            tiboEvent = null;
            snapshot.FiveHour.Remaining = 9;
            var afterReset = new UsageSnapshot { RefreshedAt = DateTimeOffset.Now, ResetCreditsAvailable = 1 };
            afterReset.FiveHour.Remaining = 100;
            afterReset.Weekly.Remaining = 100;
            DetectDelivery(afterReset);
            if (FormatRightStatus() != "额度已恢复") throw new Exception("Credit recovery detection failed");
            RebuildFrame(false);
            frame.Save(Path.Combine(output, "reset-restored.png"));
            var parsed = CodexRateLimitReader.ParseForVerification("{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":82,\"windowDurationMins\":10080}},\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":[]}}}");
            if (parsed.ResetCreditsAvailable != 3 || parsed.Weekly.Remaining != 18) throw new Exception("Reset credit parser failed");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: task states, no quota animation repaint, stale values, reset threshold, reset count, state priority, credit recovery, app-server parser");
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
                using (var brush = new SolidBrush(background)) graphics.FillRectangle(brush, area);
                DrawStatusIcon(graphics);
            }
            Invalidate(new Rectangle((int)(area.X * displayScale), 0,
                (int)Math.Ceiling(area.Width * displayScale), Height), false);
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
                "更新于 {0:HH:mm:ss} · {1}秒前 · {2}", snapshot.RefreshedAt.LocalDateTime, age, suffix);
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
                    deliveredUntil = null;
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
                deliveredUntil = DateTimeOffset.Now.AddMinutes(5);
        }

        private static bool IncreasedSubstantially(int? before, int? after)
        {
            return before.HasValue && after.HasValue &&
                   (after.Value >= 98 || after.Value - before.Value >= 20);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (frame == null)
                RebuildFrame(false);
            if (frame != null) e.Graphics.DrawImage(frame, ClientRectangle);
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
            var next = new Bitmap(GetLogicalWidth(), WidgetHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(next))
                PaintFrame(graphics);
            Bitmap previous = frame;
            frame = next;
            if (previous != null) previous.Dispose();
            if (requestPaint) Invalidate(false);
        }

        private void PaintFrame(Graphics graphics)
        {
            graphics.Clear(background);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

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
                using (var brush = new SolidBrush(background))
                    graphics.FillEllipse(brush, new Rectangle(19, 19, 10, 10));
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
                using (var brush = new SolidBrush(background)) graphics.FillEllipse(brush, 19, 19, 10, 10);
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
            Size size = TextRenderer.MeasureText(g, value, font, new Size(Int32.MaxValue, WidgetHeight),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int y = (WidgetHeight - size.Height) / 2;
            TextRenderer.DrawText(g, value, font, new Point(x, y), color, background,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            x += size.Width;
        }

        private string FormatRightStatus()
        {
            if (deliveredUntil.HasValue) return "额度已恢复";
            if (tiboEvent != null)
            {
                TimeSpan left = tiboEvent.TargetAt - DateTimeOffset.Now;
                if (left <= TimeSpan.Zero) return "Tibo 等待到账";
                if (left.TotalDays >= 1)
                    return String.Format(CultureInfo.InvariantCulture, "Tibo {0}天 {1:00}:{2:00}:{3:00}",
                        (int)left.TotalDays, left.Hours, left.Minutes, left.Seconds);
                return String.Format(CultureInfo.InvariantCulture, "Tibo {0:00}:{1:00}:{2:00}",
                    (int)left.TotalHours, left.Minutes, left.Seconds);
            }
            if (!IsQuotaStale() && snapshot.Weekly.Remaining.HasValue && snapshot.Weekly.Remaining.Value < 20 &&
                snapshot.ResetCreditsAvailable.HasValue && snapshot.ResetCreditsAvailable.Value > 0)
                return "可重置 ×" + snapshot.ResetCreditsAvailable.Value.ToString(CultureInfo.InvariantCulture);
            return String.Empty;
        }

        private int GetLogicalWidth()
        {
            string status = FormatRightStatus();
            if (String.IsNullOrEmpty(status)) return CompactWidth + StatusExtraWidth;
            return (status.StartsWith("Tibo", StringComparison.Ordinal) ? CountdownWidth : StatusWidth) + StatusExtraWidth;
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
            return Color.FromArgb(foreground.R, foreground.G, foreground.B).Blend(background, 0.45f);
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
            if (BackColor != background) BackColor = background;
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
            if (!barVisible) return;
            float scale = 1f;
            try { scale = Math.Max(1f, NativeMethods.GetDpiForWindow(current) / 96f); } catch { }
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
        }

        private void OnWidgetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragStart = Cursor.Position;
            dragOffsetStart = leftOffset;
            Capture = true;
        }

        private void OnWidgetMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            leftOffset = Math.Max(0, dragOffsetStart + Cursor.Position.X - dragStart.X);
            AttachAndPosition();
        }

        private void OnWidgetMouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            Capture = false;
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
                return result;
            }
            catch { return empty; }
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
