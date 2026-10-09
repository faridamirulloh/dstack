using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UsageLimitsMonitor
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 2 && args[0] == "--smoke-test")
                {
                    string root = Path.Combine(Path.GetFullPath(args[1]), "smoke-" + Guid.NewGuid().ToString("N"));
                    var store = new StateStore(root);
                    var sample = new Settings { StartWithWindows = false };
                    foreach (string label in new[] { "Personal Plus", "Spare Free", "Work Plus", "Travel Free" })
                    {
                        var account = new Account { Label = label, Plan = label.Contains("Plus") ? "plus" : "free" };
                        account.Last = Usage.Parse(Json.Read(@"{""rateLimits"":{""limitName"":""Codex"",""primary"":{""usedPercent"":18,""windowDurationMins"":300,""resetsAt"":2000000000},""secondary"":{""usedPercent"":100,""windowDurationMins"":10080},""credits"":{""balance"":""12.50""}}}"), Json.Now);
                        sample.Accounts.Add(account);
                    }
                    store.Save(sample);
                    using (var form = new Dashboard(store, sample, null, true, true)) { Application.Run(form); return form.SmokePassed ? 0 : 1; }
                }
                string sid = WindowsIdentity.GetCurrent().User.Value;
                bool first;
                using (var mutex = new Mutex(true, "Local\\UsageLimitsMonitor-" + sid, out first))
                {
                    if (!first)
                    {
                        try { using (var signal = EventWaitHandle.OpenExisting("Local\\UsageLimitsMonitorShow-" + sid)) signal.Set(); }
                        catch (WaitHandleCannotBeOpenedException) { }
                        return 0;
                    }
                    using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\UsageLimitsMonitorShow-" + sid))
                    {
                        var store = new StateStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageLimitsMonitor"));
                        var settings = store.Load();
                        using (var form = new Dashboard(store, settings, signal, !args.Contains("--startup"), false)) Application.Run(form);
                    }
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("The monitor could not start. Your stored files have been preserved.\n\n" + error.Message,
                    "Usage Limits Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal static class Startup
    {
        public static void Apply(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("UsageLimitsMonitor", "\"" + Application.ExecutablePath + "\" --startup", RegistryValueKind.String);
                else key.DeleteValue("UsageLimitsMonitor", false);
            }
        }
    }
    internal static class Theme
    {
        public static bool Dark = true;
        private static bool UseDark { get { return Dark && !SystemInformation.HighContrast; } }
        public static Color Background { get { return UseDark ? Color.FromArgb(24, 26, 31) : SystemColors.Control; } }
        public static Color Surface { get { return UseDark ? Color.FromArgb(33, 36, 43) : SystemColors.Window; } }
        public static Color Text { get { return UseDark ? Color.FromArgb(240, 243, 248) : SystemColors.WindowText; } }
        public static Color Muted { get { return UseDark ? Color.FromArgb(183, 193, 207) : SystemColors.GrayText; } }
        public static Color Border { get { return UseDark ? Color.FromArgb(76, 83, 97) : SystemColors.ControlDark; } }
        public static Color Selection { get { return UseDark ? Color.FromArgb(39, 77, 115) : SystemColors.Highlight; } }
        public static Color Accent { get { return UseDark ? Color.FromArgb(137, 198, 255) : Color.FromArgb(23, 88, 145); } }
        public static Color Critical { get { return UseDark ? Color.FromArgb(255, 153, 160) : Color.FromArgb(154, 25, 34); } }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        private static void TitleBar(object sender, EventArgs args)
        {
            var form = (Form)sender;
            int dark = UseDark ? 1 : 0;
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        }
        private static void ComboItem(object sender, DrawItemEventArgs args)
        {
            var combo = (ComboBox)sender;
            bool selected = (args.State & DrawItemState.Selected) != 0;
            using (var fill = new SolidBrush(selected ? Selection : Surface)) args.Graphics.FillRectangle(fill, args.Bounds);
            string text = args.Index >= 0 ? combo.GetItemText(combo.Items[args.Index]) : combo.Text;
            TextRenderer.DrawText(args.Graphics, text, combo.Font, args.Bounds, selected ? SystemColors.HighlightText : Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            args.DrawFocusRectangle();
        }
        public static void Apply(Control control)
        {
            control.BackColor = control is TextBoxBase || control is ListControl || control is DailyTokenChart ? Surface : Background;
            control.ForeColor = Text;
            var button = control as Button;
            if (button != null)
            {
                button.UseVisualStyleBackColor = false; button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border; button.FlatAppearance.MouseOverBackColor = Selection;
                button.BackColor = Surface;
            }
            var combo = control as ComboBox;
            if (combo != null)
            {
                combo.FlatStyle = FlatStyle.Flat; combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.DrawItem -= ComboItem; combo.DrawItem += ComboItem;
            }
            var grid = control as DataGridView;
            if (grid != null)
            {
                grid.EnableHeadersVisualStyles = false; grid.BackgroundColor = Surface; grid.GridColor = Border;
                grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Selection; grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Background; grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Background; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            }
            var menu = control as ToolStrip;
            if (menu != null)
            {
                menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
                foreach (ToolStripItem item in menu.Items) item.ForeColor = Text;
            }
            foreach (Control child in control.Controls) Apply(child);
            var form = control as Form;
            if (form != null)
            {
                form.HandleCreated -= TitleBar; form.HandleCreated += TitleBar;
                if (form.IsHandleCreated) TitleBar(form, EventArgs.Empty);
            }
            control.Invalidate();
        }
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Background; } }
            public override Color ImageMarginGradientBegin { get { return Background; } }
            public override Color ImageMarginGradientMiddle { get { return Background; } }
            public override Color ImageMarginGradientEnd { get { return Background; } }
            public override Color MenuItemSelected { get { return Selection; } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Border; } }
        }
    }
    internal sealed class AccountToolTip : Form
    {
        private readonly Label lines = new Label { AutoSize = true, UseMnemonic = false, BackColor = Color.Transparent, Location = new Point(7, 4) };
        public string Summary { get { return lines.Text; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ClassStyle |= 0x20000; value.ExStyle |= 0x08000080; return value; } // Shadow, no activation, tool window.
        }
        public void ApplyTheme()
        {
            Font = SystemFonts.StatusFont; BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            lines.ForeColor = ForeColor; Invalidate(true);
        }
        protected override void OnPaintBackground(PaintEventArgs args)
        {
            base.OnPaintBackground(args);
            ControlPaint.DrawBorder(args.Graphics, ClientRectangle, Theme.Border, ButtonBorderStyle.Solid);
        }
        public AccountToolTip()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; Padding = new Padding(1);
            AccessibleRole = AccessibleRole.ToolTip; AccessibleName = "All account quota summaries";
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent }; scroll.Controls.Add(lines); Controls.Add(scroll);
            ApplyTheme();
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; } // WM_MOUSEACTIVATE: never focus the app.
            base.WndProc(ref message);
        }
        public void UpdateSummary(string text, Point anchor)
        {
            ApplyTheme();
            if (lines.Text != text) lines.Text = text;
            var area = Screen.FromPoint(anchor).WorkingArea;
            var preferred = lines.PreferredSize;
            ClientSize = new Size(Math.Min(preferred.Width + 16, Math.Max(1, area.Width - 16)),
                Math.Min(preferred.Height + 12, Math.Max(1, area.Height - 16)));
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, anchor.X + 12)),
                Math.Max(area.Top, Math.Min(area.Bottom - Height, anchor.Y - Height - 12)));
        }
    }
    internal static class NumericIcon
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(IntPtr handle);
        public static Icon Create(double? remaining, int size = 32)
        {
            using (var bitmap = new Bitmap(size, size))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(!remaining.HasValue ? Color.FromArgb(83, 91, 103) : remaining <= 5 ? Color.FromArgb(166, 42, 52) : remaining <= 20 ? Color.FromArgb(134, 83, 8) : Color.FromArgb(48, 60, 76)))
            using (var outline = new Pen(Color.FromArgb(160, 190, 201, 217), Math.Max(1, size / 32f)))
            using (var font = new Font("Segoe UI", size * (remaining >= 100 ? 0.53F : 0.70F), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Color.White))
            using (var shape = new GraphicsPath())
            using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float corner = size * 0.34F, edge = size - 1;
                shape.AddArc(0.5F, 0.5F, corner, corner, 180, 90); shape.AddArc(edge - corner, 0.5F, corner, corner, 270, 90);
                shape.AddArc(edge - corner, edge - corner, corner, corner, 0, 90); shape.AddArc(0.5F, edge - corner, corner, corner, 90, 90); shape.CloseFigure();
                graphics.FillPath(background, shape); graphics.DrawPath(outline, shape);
                string text = remaining.HasValue ? Math.Floor(remaining.Value).ToString("0", CultureInfo.InvariantCulture) : "?";
                graphics.DrawString(text, font, ink, new RectangleF(0, -0.5F, size, size), format);
                IntPtr handle = bitmap.GetHicon();
                try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }
    internal sealed class Connection
    {
        public CodexClient Client;
        public string Error;
        public int Failures;
        public long NextTry;
        public bool Verified;
    }
    internal sealed class Dashboard : Form
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private readonly StateStore store;
        private readonly Settings settings;
        private readonly EventWaitHandle showSignal;
        private readonly bool smoke, showAtStart;
        private readonly Dictionary<string, Connection> connections = new Dictionary<string, Connection>();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly AccountToolTip accountTip = new AccountToolTip();
        private readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 200 };
        private readonly DataGridView grid = new DataGridView();
        private readonly Label footer = new Label();
        private readonly Label heading = new Label();
        private readonly FlowLayoutPanel toolbar = new FlowLayoutPanel();
        private readonly List<Button> actions = new List<Button>();
        private readonly Queue<Tuple<string, string>> notices = new Queue<Tuple<string, string>>();
        private readonly Stopwatch uptime = Stopwatch.StartNew();
        public bool SmokePassed { get; private set; }
        private Icon numericIcon;
        private string iconText, notice;
        private bool busy, quitting, dialogOpen, hoverPending;
        private Point hoverAnchor;
        private long hoverAt, leaveAt, lastPaint, lastNotice;
        private CancellationTokenSource loginCancellation;
        protected override bool ShowWithoutActivation { get { return smoke; } }

        public Dashboard(StateStore stateStore, Settings state, EventWaitHandle signal, bool show, bool test)
        {
            store = stateStore; settings = state; showSignal = signal; showAtStart = show; smoke = test;
            Theme.Dark = settings.DarkTheme;
            Text = "Usage Limits Monitor" + (smoke ? " - test data" : "");
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1280, 460);
            MinimumSize = new Size(720, 360);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            heading.Dock = DockStyle.Fill;
            heading.Text = "Codex usage across your accounts";
            heading.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            heading.TextAlign = ContentAlignment.MiddleLeft;
            toolbar.Dock = DockStyle.Fill;
            toolbar.WrapContents = true;
            AddButton("&Add account", async delegate { await Enroll(null); });
            AddButton("&Reconnect", async delegate { var account = Selection(); if (account != null) await Enroll(account); });
            AddButton("Re&name", Rename);
            AddButton("Re&move", async delegate { await RemoveSelected(); });
            AddButton("Refresh &now", async delegate { await RefreshAll(true); });
            AddButton("&Pin quota", PinSelected);
            AddButton("Subs&cription", EditSubscription);
            AddButton("&History", ShowHistory);
            AddButton("&Settings", ShowSettings);
            var quit = new Button { Text = "&Quit", AutoSize = true }; quit.Click += delegate { Quit(); }; toolbar.Controls.Add(quit);
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false; grid.RowHeadersVisible = false; grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = SystemColors.Window; grid.BorderStyle = BorderStyle.FixedSingle;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AccessibleName = "Accounts, quota windows, available resets, credits, and subscription dates";
            string[] names = { "Account", "Plan", "Buckets / windows", "Remaining", "Resets in", "Reset date / time", "Credits", "Available resets", "Subscription", "Status / last update" };
            int[] weights = { 90, 55, 125, 100, 110, 150, 80, 85, 205, 155 };
            for (int i = 0; i < names.Length; i++)
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = names[i], FillWeight = weights[i], MinimumWidth = weights[i], SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.CellDoubleClick += delegate { PinSelected(); };
            footer.Dock = DockStyle.Fill; footer.TextAlign = ContentAlignment.MiddleLeft;
            footer.AutoEllipsis = true;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.Controls.Add(heading, 0, 0); layout.Controls.Add(toolbar, 0, 1); layout.Controls.Add(grid, 0, 2); layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show accounts", null, delegate { ShowDashboard(); });
            menu.Items.Add("Refresh now", null, async delegate { await RefreshAll(true); });
            menu.Items.Add("Settings", null, delegate { ShowDashboard(); ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Quit", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu;
            menu.Opening += delegate { HideAccountTip(); };
            tray.MouseMove += delegate
            {
                if (accountTip.Visible || hoverPending || dialogOpen || menu.Visible) return;
                hoverPending = true; hoverAnchor = Cursor.Position; hoverAt = uptime.ElapsedMilliseconds;
            };
            tray.MouseClick += delegate(object sender, MouseEventArgs args) { HideAccountTip(); if (args.Button == MouseButtons.Left) ShowDashboard(); };
            tray.BalloonTipClicked += delegate { ShowDashboard(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (!quitting && args.CloseReason != CloseReason.WindowsShutDown && args.CloseReason != CloseReason.TaskManagerClosing)
                { args.Cancel = true; Hide(); HideAccountTip(); }
            };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) { Hide(); WindowState = FormWindowState.Normal; } };
            Shown += async delegate
            {
                if (smoke) { await SmokeCheck(); return; }
                if (!showAtStart) Hide();
                await RefreshAll(true);
            };
            if (string.IsNullOrWhiteSpace(settings.CodexPath)) settings.CodexPath = CodexClient.FindExecutable();
            if (!smoke)
            {
                try { store.DeleteLegacyHistory(); }
                catch (Exception error) { notice = "Old local history could not be removed: " + error.Message; }
                store.Save(settings);
                try { Startup.Apply(settings.StartWithWindows); }
                catch (Exception) { notice = "Windows startup registration failed. Retry from Settings."; }
            }
            ApplyTheme(); RenderRows(); tray.Visible = true;
            clock.Tick += async delegate { await Tick(); };
            clock.Start();
        }
        private void AddButton(string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(75, 28) };
            button.Click += delegate { click(); }; toolbar.Controls.Add(button); actions.Add(button);
        }
        private Connection State(Account account)
        {
            Connection state;
            if (!connections.TryGetValue(account.Id, out state)) { state = new Connection(); connections.Add(account.Id, state); }
            return state;
        }
        private Account Selection() { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Account; }
        private bool Fresh(Account account) { return account.Last != null && (smoke || State(account).Verified) && State(account).Error == null && Json.Now - account.Last.At <= 150; }
        private void ApplyTheme()
        {
            Theme.Dark = settings.DarkTheme;
            Theme.Apply(this); accountTip.ApplyTheme(); Theme.Apply(tray.ContextMenuStrip);
        }
        private async Task Work(Func<Task> operation)
        {
            if (busy || quitting) return;
            busy = true; foreach (var button in actions) button.Enabled = false;
            try { await operation(); }
            catch (Exception error) { if (!quitting) Error(error.Message); }
            finally
            {
                busy = false;
                if (!IsDisposed && !quitting) { foreach (var button in actions) button.Enabled = true; RenderRows(); }
            }
        }
        private void Error(string message)
        {
            notice = message;
            HideAccountTip();
            dialogOpen = true;
            try { MessageBox.Show(this, message, "Usage Limits Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { dialogOpen = false; }
        }
        private async Task<CodexClient> Client(Account account)
        {
            var state = State(account);
            if (state.Client == null || !state.Client.Running)
            {
                if (state.Client != null) state.Client.Dispose();
                state.Client = await CodexClient.Start(settings.CodexPath, store.Profile(account.Id));
            }
            return state.Client;
        }
        private void AcceptIdentity(Account account, Dictionary<string, object> metadata)
        {
            string identity = CodexClient.Identity(metadata);
            if (!string.IsNullOrEmpty(account.Identity) && account.Identity != identity)
                throw new InvalidOperationException("A different account is signed in to this profile. Reconnect using its original account, or remove the profile and add it again.");
            if (settings.Accounts.Any(other => other.Id != account.Id && other.Identity == identity))
                throw new InvalidOperationException("This account is already in the monitor. Reconnect and choose another account in the browser, or remove this duplicate profile.");
            account.Identity = identity;
            account.Plan = Usage.Nonempty(Json.Text(metadata, "planType"), "Unknown");
        }
        private async Task RefreshOne(Account account)
        {
            var state = State(account);
            try
            {
                var client = await Client(account);
                AcceptIdentity(account, await client.ReadAccount());
                var snapshot = Usage.Parse(await client.Request("account/rateLimits/read", new { }), Json.Now);
                if (quitting) return;
                // Persist the ledger before notifications. Roll it back if saving fails so a later refresh can retry.
                var previousAlerts = Json.Serializer().Deserialize<Dictionary<string, AlertState>>(Json.Write(account.Alerts));
                var previousSnapshot = account.Last;
                var alerts = Usage.Alerts(account, snapshot);
                account.Last = snapshot;
                try { store.Save(settings); }
                catch { account.Alerts = previousAlerts; account.Last = previousSnapshot; throw; }
                state.Error = null; state.Failures = 0; state.Verified = true; state.NextTry = Json.Now + 60;
                foreach (string alert in alerts) notices.Enqueue(Tuple.Create(account.Label, alert));
            }
            catch (Exception error)
            {
                state.Error = error.Message; state.Failures++;
                state.NextTry = Json.Now + Math.Min(900, 60 * (1 << Math.Min(4, state.Failures - 1)));
                if (state.Client != null) { state.Client.Dispose(); state.Client = null; }
            }
        }
        private async Task RefreshAll(bool manual)
        {
            if (smoke || busy || quitting || dialogOpen) return;
            await Work(async delegate
            {
                // ponytail: serialize a few accounts; use bounded concurrency if larger lists make a sweep too slow.
                foreach (var account in settings.Accounts.ToArray())
                {
                    if (quitting) break;
                    if (manual || State(account).NextTry <= Json.Now) await RefreshOne(account);
                    if (!quitting) RenderRows();
                }
            });
        }
        private async Task Enroll(Account account)
        {
            if (busy || quitting || smoke) return;
            HideAccountTip();
            if (!File.Exists(settings.CodexPath)) { ShowSettings(); if (!File.Exists(settings.CodexPath)) return; }
            if (account == null)
            {
                dialogOpen = true;
                string label;
                try { label = Prompt.Ask(this, "Add account", "Choose a display label (used in notifications):", "Account " + (settings.Accounts.Count + 1)); }
                finally { dialogOpen = false; }
                if (label == null) return;
                account = new Account { Label = label }; settings.Accounts.Add(account);
                try { store.Save(settings); }
                catch (Exception error) { settings.Accounts.Remove(account); Error(error.Message); return; }
            }
            await Work(async delegate
            {
                loginCancellation = new CancellationTokenSource();
                using (var progress = new Form { Text = "Sign in: " + account.Label, ClientSize = new Size(460, 155), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
                {
                    var text = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "Complete sign-in in your browser.\n\nCheck which account the browser selects. Use another account there if needed. No password is entered into this monitor." };
                    var cancel = new Button { Text = "Cancel sign-in", Dock = DockStyle.Bottom, Height = 34 };
                    cancel.Click += delegate { loginCancellation.Cancel(); cancel.Enabled = false; text.Text = "Cancelling sign-in..."; };
                    progress.FormClosing += delegate(object sender, FormClosingEventArgs args) { if (loginCancellation != null) loginCancellation.Cancel(); };
                    progress.Controls.Add(text); progress.Controls.Add(cancel);
                    Theme.Apply(progress);
                    progress.Show(this); dialogOpen = true;
                    try
                    {
                        var client = await Client(account);
                        await client.Login(address => Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }), loginCancellation.Token);
                        AcceptIdentity(account, await client.ReadAccount());
                        store.Save(settings);
                        await RefreshOne(account);
                    }
                    catch (OperationCanceledException) { State(account).Error = "Sign-in cancelled. Use Reconnect to finish."; }
                    catch (Exception error) { State(account).Error = error.Message; notice = error.Message; }
                    finally
                    {
                        loginCancellation.Dispose(); loginCancellation = null;
                        progress.Close(); dialogOpen = false;
                    }
                }
            });
        }
        private void Rename()
        {
            var selected = Selection(); if (selected == null || busy) return;
            HideAccountTip();
            dialogOpen = true;
            string label;
            try { label = Prompt.Ask(this, "Rename account", "Display label:", selected.Label); }
            finally { dialogOpen = false; }
            if (label == null) return;
            string previous = selected.Label; selected.Label = label;
            try { store.Save(settings); }
            catch (Exception error) { selected.Label = previous; Error(error.Message); }
            RenderRows();
        }
        private async Task RemoveSelected()
        {
            var selected = Selection(); if (selected == null || busy) return;
            HideAccountTip();
            dialogOpen = true;
            DialogResult answer;
            try { answer = MessageBox.Show(this, "Remove " + selected.Label + "?\n\nThis signs out only its monitor profile and deletes its saved settings and quota snapshot. Your other Codex logins are not changed.", "Remove account", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning); }
            finally { dialogOpen = false; }
            if (answer != DialogResult.OK) return;
            await Work(async delegate
            {
                var account = selected;
                var client = await Client(account);
                await client.Request("account/logout", new { });
                client.Dispose(); State(account).Client = null;
                store.DeleteProfile(account.Id);
                settings.Accounts.Remove(account);
                try { store.Save(settings); store.Save(settings); }
                catch { settings.Accounts.Add(account); throw; }
                connections.Remove(account.Id);
                notices.Clear();
                if (settings.PinnedAccount == account.Id) { settings.PinnedAccount = null; settings.PinnedQuota = null; store.Save(settings); }
            });
        }
        private void PinSelected()
        {
            var selected = Selection(); if (selected == null || busy) return;
            var quotas = Usage.AccountQuotas(selected); if (quotas.Count == 0) return;
            HideAccountTip();
            Quota chosen;
            dialogOpen = true;
            try { chosen = quotas.Count == 1 ? quotas[0] : Prompt.PickQuota(this, selected, settings.PinnedAccount == selected.Id ? settings.PinnedQuota : null); }
            finally { dialogOpen = false; }
            if (chosen == null) return;
            string account = settings.PinnedAccount, quota = settings.PinnedQuota;
            settings.PinnedAccount = selected.Id; settings.PinnedQuota = chosen.Key;
            try { store.Save(settings); }
            catch (Exception error) { settings.PinnedAccount = account; settings.PinnedQuota = quota; Error(error.Message); }
            RenderRows();
        }
        private void EditSubscription()
        {
            var selected = Selection(); if (selected == null || busy) return;
            if (Usage.IsFree(selected)) { Error("Free accounts have no paid subscription date."); return; }
            HideAccountTip();
            dialogOpen = true;
            try
            {
                using (var form = new SubscriptionForm(selected))
                {
                    if (form.ShowDialog(this) != DialogResult.OK) return;
                    long? previousDate = selected.ManualSubscriptionAt;
                    string previousKind = selected.ManualSubscriptionKind;
                    selected.ManualSubscriptionAt = form.SubscriptionAt; selected.ManualSubscriptionKind = form.SubscriptionKind;
                    try { store.Save(settings); }
                    catch { selected.ManualSubscriptionAt = previousDate; selected.ManualSubscriptionKind = previousKind; throw; }
                }
            }
            catch (Exception error) { Error(error.Message); }
            finally { dialogOpen = false; RenderRows(); }
        }
        private void ShowHistory()
        {
            var selected = Selection(); if (selected == null || busy) return;
            HideAccountTip();
            dialogOpen = true;
            try
            {
                using (var form = new HistoryForm(selected, async delegate
                {
                    var client = await Client(selected);
                    AcceptIdentity(selected, await client.ReadAccount());
                    return Usage.ParseHistory(await client.Request("account/usage/read", new { }));
                })) form.ShowDialog(this);
            }
            catch (Exception error) { Error(error.Message); }
            finally { dialogOpen = false; }
        }
        private void ShowSettings()
        {
            if (busy) return;
            HideAccountTip();
            dialogOpen = true;
            try
            {
                using (var form = new SettingsForm(settings, store.Root))
                {
                    if (form.ShowDialog(this) != DialogResult.OK) return;
                    string oldPath = settings.CodexPath; bool oldStartup = settings.StartWithWindows, oldDark = settings.DarkTheme;
                    settings.CodexPath = form.Executable; settings.StartWithWindows = form.StartupEnabled; settings.DarkTheme = form.DarkEnabled;
                    try { store.Save(settings); if (!smoke) Startup.Apply(settings.StartWithWindows); }
                    catch (Exception)
                    {
                        settings.CodexPath = oldPath; settings.StartWithWindows = oldStartup; settings.DarkTheme = oldDark; store.Save(settings);
                        throw;
                    }
                    if (oldPath != settings.CodexPath)
                        foreach (var state in connections.Values)
                        { if (state.Client != null) state.Client.Dispose(); state.Client = null; state.NextTry = 0; state.Verified = false; }
                    notice = null;
                }
            }
            catch (Exception error) { Error(error.Message); }
            finally { dialogOpen = false; ApplyTheme(); RenderRows(); }
        }
        private void RenderRows()
        {
            var selected = Selection(); string id = selected == null ? null : selected.Id;
            int scroll = grid.FirstDisplayedScrollingRowIndex;
            grid.Rows.Clear();
            foreach (var account in settings.Accounts)
            {
                var quotas = Usage.AccountQuotas(account);
                string status = State(account).Error;
                if (status != null) status = "Refresh failed; saved data. " + status;
                else if (account.Last == null) status = "Not connected. Use Reconnect.";
                else status = Fresh(account) ? "Updated " + Json.LocalTime(account.Last.At).ToString("HH:mm:ss") : "Stale: " + Json.LocalTime(account.Last.At).ToString("MMM d HH:mm");
                if (account.Last != null && State(account).Error != null) status += " Last update: " + Json.LocalTime(account.Last.At).ToString("MMM d HH:mm");
                var reached = quotas.Where(q => !string.IsNullOrWhiteSpace(q.LimitState)).Select(q => q.Bucket + ": " + q.LimitState).Distinct().ToArray();
                if (reached.Length > 0) status += " [" + string.Join(" | ", reached) + "]";
                long now = Json.Now;
                int index = grid.Rows.Add(account.Label, Usage.Nonempty(account.Plan, "Unknown"), Usage.WindowLabels(account),
                    Usage.QuotaValues(account, Usage.RemainingText, "Unavailable"), Usage.QuotaValues(account, q => Usage.ResetCountdown(q, now), "Not reported"),
                    Usage.QuotaValues(account, Usage.ResetDate, "Not reported"), Usage.AccountCredits(account), Usage.AvailableResetsText(account), Usage.SubscriptionText(account, now), status);
                var row = grid.Rows[index]; row.Tag = account;
                if (!Fresh(account)) row.DefaultCellStyle.ForeColor = Theme.Muted;
                else if (quotas.Any(q => q.Remaining <= 5)) row.DefaultCellStyle.ForeColor = Theme.Critical;
                if (account.Id == id) grid.CurrentCell = row.Cells[0];
            }
            if (scroll >= 0 && scroll < grid.RowCount) grid.FirstDisplayedScrollingRowIndex = scroll;
            UpdateIcon();
            if (accountTip.Visible) UpdateAccountTip();
            heading.Text = settings.Accounts.Count == 0 ? "Add your first ChatGPT account" : "Codex usage across " + settings.Accounts.Count + " accounts";
            footer.Text = (notice ?? "Refresh: 60 seconds. History: OpenAI daily tokens. Credits are separate from quota; unavailable does not mean zero.") + "\n" +
                (settings.Accounts.Count == 0 ? "Requires a native codex.exe. Sign in separately for each account. This does not monitor ordinary ChatGPT message limits." : "Tray number: " + iconText + ". Select an account, then Pin quota to choose a window or Subscription to set a manual date. Close hides; Quit stops monitoring.");
        }
        private void UpdateIcon()
        {
            Account account = settings.Accounts.FirstOrDefault(a => a.Id == settings.PinnedAccount);
            Quota quota = account == null || account.Last == null ? null : account.Last.Quotas.FirstOrDefault(q => q.Key == settings.PinnedQuota);
            if (string.IsNullOrEmpty(settings.PinnedAccount))
            {
                account = settings.Accounts.FirstOrDefault(a => a.Last != null && a.Last.Quotas.Any(q => q.Remaining.HasValue));
                if (account != null) quota = account.Last.Quotas.First(q => q.Remaining.HasValue);
            }
            double? value = account != null && quota != null && Fresh(account) ? quota.Remaining : null;
            string caption = account == null || quota == null ? "No reported quota" : account.Label + " / " + quota.Bucket + " / " + quota.Window;
            string next = caption + ": " + (value.HasValue ? Math.Floor(value.Value) + "% remaining" : "unknown or stale");
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            if (iconText == next && numericIcon != null && numericIcon.Width == size) return;
            var replacement = NumericIcon.Create(value, size);
            tray.Icon = replacement; Icon = replacement;
            if (numericIcon != null) numericIcon.Dispose();
            numericIcon = replacement; iconText = next;
            // Keep every full account line in a themed tooltip without NotifyIcon.Text's character cap.
            tray.Text = "";
        }
        private async Task Tick()
        {
            if (quitting) return;
            if (showSignal != null && showSignal.WaitOne(0)) ShowDashboard();
            long ticks = uptime.ElapsedMilliseconds;
            var nearIcon = new Rectangle(hoverAnchor.X - 24, hoverAnchor.Y - 24, 48, 48).Contains(Cursor.Position);
            if (hoverPending)
            {
                if (!nearIcon) hoverPending = false;
                else if (ticks - hoverAt >= 450) { hoverPending = false; ShowAccountTip(); }
            }
            if (accountTip.Visible)
            {
                if (dialogOpen || tray.ContextMenuStrip.Visible) HideAccountTip();
                else if (accountTip.Bounds.Contains(Cursor.Position) || nearIcon) leaveAt = 0;
                else if (leaveAt == 0) leaveAt = ticks;
                else if (ticks - leaveAt > 650) HideAccountTip();
            }
            if (Json.Now - lastPaint >= 5) { lastPaint = Json.Now; if (!dialogOpen) RenderRows(); }
            if (!smoke && notices.Count > 0 && Json.Now - lastNotice >= 12)
            {
                var next = notices.Dequeue(); lastNotice = Json.Now;
                tray.ShowBalloonTip(8000, next.Item1, next.Item2, ToolTipIcon.Info);
            }
            if (!smoke && !busy && !dialogOpen && settings.Accounts.Any(a => State(a).NextTry <= Json.Now)) await RefreshAll(false);
        }
        private void UpdateAccountTip()
        {
            string text = settings.Accounts.Count == 0 ? "No accounts connected. Click the tray icon to add one." :
                string.Join(Environment.NewLine, settings.Accounts.Select(a => Usage.AccountTip(a, Fresh(a), Json.Now)));
            accountTip.UpdateSummary(text, hoverAnchor);
        }
        private void ShowAccountTip()
        {
            if (quitting || dialogOpen || tray.ContextMenuStrip.Visible) return;
            hoverPending = false; leaveAt = 0;
            UpdateAccountTip();
            accountTip.Show();
        }
        private void HideAccountTip() { hoverPending = false; leaveAt = 0; accountTip.Hide(); }
        private void ShowDashboard()
        {
            if (quitting || dialogOpen) return;
            HideAccountTip();
            if (!Visible)
            {
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, Cursor.Position.X - Width / 2)), Math.Max(area.Top, area.Bottom - Height));
                ShowInTaskbar = true; Show();
            }
            Activate();
        }
        private void Quit() { quitting = true; if (loginCancellation != null) loginCancellation.Cancel(); Close(); }
        protected override void OnFormClosed(FormClosedEventArgs args)
        {
            quitting = true; clock.Stop(); clock.Dispose(); tray.Visible = false;
            accountTip.Dispose();
            tray.ContextMenuStrip.Dispose(); tray.Dispose();
            foreach (var state in connections.Values) if (state.Client != null) state.Client.Dispose();
            if (numericIcon != null) numericIcon.Dispose();
            base.OnFormClosed(args);
        }
        private async Task SmokeCheck()
        {
            await Task.Delay(500);
            string parent = Path.GetDirectoryName(store.Root);
            foreach (var account in settings.Accounts)
            {
                account.Last.Quotas[0].ResetsAt = Json.Now + 7200;
                if (account.Plan == "free") foreach (var quota in account.Last.Quotas) quota.Credits = "Unavailable";
            }
            RenderRows();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(parent, "dashboard-smoke.png")); }
            bool darkWorks = settings.DarkTheme && BackColor == Theme.Background && grid.DefaultCellStyle.ForeColor == Theme.Text;
            settings.DarkTheme = false; ApplyTheme(); RenderRows();
            bool lightWorks = BackColor == SystemColors.Control && grid.DefaultCellStyle.ForeColor == SystemColors.WindowText;
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(parent, "light-theme-smoke.png")); }
            settings.DarkTheme = true; ApplyTheme(); RenderRows();
            var selected = settings.Accounts[0];
            settings.PinnedAccount = selected.Id; settings.PinnedQuota = selected.Last.Quotas[1].Key;
            UpdateIcon(); bool zeroWorks = iconText.Contains("0% remaining");
            State(selected).Error = "Test connection failure";
            UpdateIcon(); bool staleWorks = iconText.Contains("unknown or stale");
            State(selected).Error = null;
            var today = DateTime.UtcNow.Date;
            var fixture = new ServiceUsage { LifetimeTokens = 2350000, PeakDailyTokens = 99000,
                Days = Enumerable.Range(0, 40).Where(i => i != 4).Select(i => new DailyTokens {
                    Date = today.AddDays(-i), Tokens = i == 2 ? (long?)null : i == 0 ? 0 : 18000 + i * 3139 % 80000 }).ToList() };
            bool failHistory = false, serviceWorks, rangesWork, unavailableWorks;
            using (var remote = new HistoryForm(selected, delegate
            {
                if (failHistory) throw new IOException("Synthetic offline response");
                return Task.FromResult(fixture);
            }))
            {
                var historyShown = new TaskCompletionSource<bool>();
                remote.Shown += delegate { historyShown.TrySetResult(true); };
                remote.Show(this);
                bool didShow = await Task.WhenAny(historyShown.Task, Task.Delay(2000)) == historyShown.Task;
                var chart = remote.Controls.Find("tokenChart", true).OfType<DailyTokenChart>().Single();
                var table = remote.Controls.Find("dailyUsage", true).OfType<DataGridView>().Single();
                var range = remote.Controls.Find("historyRange", true).OfType<ComboBox>().Single();
                var view = remote.Controls.Find("historyView", true).OfType<ComboBox>().Single();
                var refresh = remote.Controls.Find("historyRefresh", true).OfType<Button>().Single();
                var status = remote.Controls.Find("historyStatus", true).OfType<Label>().Single();
                serviceWorks = didShow && chart.Visible && range.SelectedIndex == 1 && chart.Points.Count == 29 &&
                    chart.Points.Any(d => d.Tokens == 0) && chart.Points.Any(d => d.Tokens == null) && !chart.Points.Any(d => d.Date == today.AddDays(-4));
                using (var bitmap = new Bitmap(remote.Width, remote.Height)) { remote.DrawToBitmap(bitmap, new Rectangle(Point.Empty, remote.Size)); bitmap.Save(Path.Combine(parent, "openai-history-smoke.png")); }
                Theme.Dark = false; Theme.Apply(remote);
                using (var bitmap = new Bitmap(remote.Width, remote.Height)) { remote.DrawToBitmap(bitmap, new Rectangle(Point.Empty, remote.Size)); bitmap.Save(Path.Combine(parent, "history-light-smoke.png")); }
                Theme.Dark = true; Theme.Apply(remote);
                remote.Size = remote.MinimumSize;
                using (var bitmap = new Bitmap(remote.Width, remote.Height)) { remote.DrawToBitmap(bitmap, new Rectangle(Point.Empty, remote.Size)); bitmap.Save(Path.Combine(parent, "history-small-smoke.png")); }
                remote.Size = new Size(820, 590);
                view.SelectedIndex = 1;
                serviceWorks &= table.Visible && !chart.Visible && table.Rows.Count == 29 &&
                    (string)table.Rows[0].Cells[1].Value == "0" && (string)table.Rows[2].Cells[1].Value == "Unavailable";
                using (var bitmap = new Bitmap(remote.Width, remote.Height)) { remote.DrawToBitmap(bitmap, new Rectangle(Point.Empty, remote.Size)); bitmap.Save(Path.Combine(parent, "history-table-smoke.png")); }
                range.SelectedIndex = 0; rangesWork = table.Rows.Count == 6 && chart.Points.Count == 6;
                range.SelectedIndex = 2; rangesWork &= table.Rows.Count == 39 && chart.Start == today.AddDays(-39);
                view.SelectedIndex = 0; range.SelectedIndex = 1;
                fixture = new ServiceUsage(); refresh.PerformClick();
                unavailableWorks = chart.Points.Count == 0 && table.Rows.Count == 0 && status.Text.Contains("unavailable");
                using (var bitmap = new Bitmap(remote.Width, remote.Height)) { remote.DrawToBitmap(bitmap, new Rectangle(Point.Empty, remote.Size)); bitmap.Save(Path.Combine(parent, "history-unavailable-smoke.png")); }
                fixture = new ServiceUsage { Days = new List<DailyTokens>() }; refresh.PerformClick();
                unavailableWorks &= chart.Points.Count == 0 && status.Text.Contains("no daily buckets");
                failHistory = true; refresh.PerformClick();
                unavailableWorks &= chart.Points.Count == 0 && table.Rows.Count == 0 && status.Text.Contains("Synthetic offline");
                remote.Close();
            }
            bool iconsWork = true;
            using (var sheet = new Bitmap(608, 230))
            using (var graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(Color.FromArgb(24, 26, 31));
                double?[] values = { 100, 70, 20, 5, 0, null };
                int[] sizes = { 16, 20, 24, 32 };
                for (int column = 0; column < values.Length; column++)
                    graphics.DrawString(values[column].HasValue ? values[column].Value + "%" : "Unknown", SystemFonts.StatusFont, Brushes.White, 45 + column * 92, 6);
                for (int row = 0; row < sizes.Length; row++)
                {
                    graphics.DrawString(sizes[row] + "px", SystemFonts.StatusFont, Brushes.White, 3, 45 + row * 48);
                    for (int column = 0; column < values.Length; column++)
                        using (var icon = NumericIcon.Create(values[column], sizes[row]))
                        using (var bitmap = icon.ToBitmap())
                        {
                            iconsWork &= icon.Width == sizes[row] && bitmap.GetPixel(0, 0).A == 0;
                            int x = 42 + column * 92, y = 32 + row * 48;
                            graphics.FillRectangle(Brushes.WhiteSmoke, x + 44, y, 42, 42);
                            graphics.DrawImageUnscaled(bitmap, x + (42 - sizes[row]) / 2, y + (42 - sizes[row]) / 2);
                            graphics.DrawImageUnscaled(bitmap, x + 44 + (42 - sizes[row]) / 2, y + (42 - sizes[row]) / 2);
                        }
                }
                sheet.Save(Path.Combine(parent, "tray-icons-smoke.png"));
            }
            using (var options = new SettingsForm(settings, store.Root))
            {
                options.Show(this);
                using (var bitmap = new Bitmap(options.Width, options.Height)) { options.DrawToBitmap(bitmap, new Rectangle(Point.Empty, options.Size)); bitmap.Save(Path.Combine(parent, "settings-smoke.png")); }
                options.Close();
            }
            selected.Last.Quotas[0].Remaining = 70; selected.Last.Quotas[0].ResetsAt = Json.Now + 5 * 86400;
            selected.Last.Quotas[1].Remaining = 90;
            settings.Accounts[1].Last.Quotas[0].Remaining = 10; settings.Accounts[1].Last.Quotas[0].ResetsAt = Json.Now + 4 * 3600;
            settings.Accounts[1].Last.Quotas[1].Remaining = 80;
            Hide(); var foreground = GetForegroundWindow(); ShowAccountTip();
            bool hoverWorks = accountTip.Visible && !Visible && GetForegroundWindow() == foreground && GetForegroundWindow() != accountTip.Handle && accountTip.Summary.Length > 127 && accountTip.Summary.Split('\n').Length == settings.Accounts.Count;
            string hoverDetails = "Tooltip visible=" + accountTip.Visible + ", dashboard visible=" + Visible + ", foreground unchanged=" + (GetForegroundWindow() == foreground) + ", tooltip is foreground=" + (GetForegroundWindow() == accountTip.Handle) + ", characters=" + accountTip.Summary.Length + ", lines=" + accountTip.Summary.Split('\n').Length;
            bool tooltipTheme = true;
            foreach (bool dark in new[] { true, false })
            {
                settings.DarkTheme = dark; ApplyTheme(); UpdateAccountTip();
                tooltipTheme &= accountTip.BackColor == Theme.Surface && accountTip.ForeColor == Theme.Text && accountTip.Font.Equals(SystemFonts.StatusFont);
                using (var bitmap = new Bitmap(accountTip.Width, accountTip.Height))
                {
                    accountTip.DrawToBitmap(bitmap, new Rectangle(Point.Empty, accountTip.Size));
                    tooltipTheme &= bitmap.GetPixel(2, 2).ToArgb() == Theme.Surface.ToArgb();
                    bitmap.Save(Path.Combine(parent, dark ? "tooltip-smoke.png" : "tooltip-light-smoke.png"));
                }
            }
            settings.DarkTheme = true; ApplyTheme(); UpdateAccountTip();
            ShowDashboard(); bool clickWorks = Visible && !accountTip.Visible;
            Close(); bool closeHides = !Visible && !IsDisposed;
            bool noLocalHistory = !Directory.Exists(Path.Combine(store.Root, "history"));
            if (!hoverWorks) File.WriteAllText(Path.Combine(parent, "hover-check.txt"), hoverDetails);
            SmokePassed = grid.RowCount == settings.Accounts.Count && hoverWorks && tooltipTheme && clickWorks && closeHides && zeroWorks && staleWorks && darkWorks && lightWorks && serviceWorks && rangesWork && unavailableWorks && iconsWork && noLocalHistory;
            File.WriteAllText(Path.Combine(parent, "ui-checks.txt"), "Rows: " + grid.RowCount + "\nHover preserves all full account lines without opening/focusing: " + hoverWorks + "\nTooltip follows selected dark/light theme: " + tooltipTheme + "\nClick opens dashboard and hides tooltip: " + clickWorks + "\nClose hides: " + closeHides + "\nPinned exhausted quota: " + zeroWorks + "\nStale tray value is unknown: " + staleWorks + "\nDark theme: " + darkWorks + "\nLight theme: " + lightWorks + "\nOpenAI chart and exact-value table: " + serviceWorks + "\n7/30/all date ranges: " + rangesWork + "\nUnavailable, empty, and failed history: " + unavailableWorks + "\nTray icons at 16/20/24/32 pixels: " + iconsWork + "\nNo local history created: " + noLocalHistory + "\nStartup registration: skipped\nNetwork and sign-in: skipped\n");
            Quit();
        }
    }

    internal static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string caption, string value)
        {
            using (var form = new Form { Text = title, ClientSize = new Size(410, 135), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var label = new Label { Text = caption, Location = new Point(12, 12), Size = new Size(386, 24) };
                var input = new TextBox { Text = value, Location = new Point(12, 40), Width = 386, MaxLength = 80, AccessibleName = "Account display label" };
                var okay = new Button { Text = "OK", Location = new Point(236, 87), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Location = new Point(321, 87), DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { label, input, okay, cancel }); form.AcceptButton = okay; form.CancelButton = cancel;
                input.TextChanged += delegate { okay.Enabled = !string.IsNullOrWhiteSpace(input.Text) && !input.Text.Any(char.IsControl); };
                Theme.Apply(form);
                return form.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
            }
        }
        public static Quota PickQuota(IWin32Window owner, Account account, string pinnedKey)
        {
            var quotas = Usage.AccountQuotas(account);
            using (var form = new Form { Text = "Pin quota: " + account.Label, ClientSize = new Size(500, 145), AutoScaleMode = AutoScaleMode.Dpi,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var label = new Label { Text = "Choose the window shown by the tray number:", Location = new Point(12, 12), AutoSize = true };
                var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 40), Width = 476, AccessibleName = "Quota window to pin" };
                foreach (var quota in quotas) choice.Items.Add(quota.Bucket + " / " + Usage.CompactWindow(quota) + " / " + Usage.RemainingText(quota));
                choice.SelectedIndex = Math.Max(0, quotas.FindIndex(q => q.Key == pinnedKey));
                var okay = new Button { Text = "Pin", Location = new Point(320, 96), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Location = new Point(405, 96), DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { label, choice, okay, cancel }); form.AcceptButton = okay; form.CancelButton = cancel;
                Theme.Apply(form);
                return form.ShowDialog(owner) == DialogResult.OK ? quotas[choice.SelectedIndex] : null;
            }
        }
    }
    internal sealed class SubscriptionForm : Form
    {
        public long? SubscriptionAt { get; private set; }
        public string SubscriptionKind { get; private set; }
        public SubscriptionForm(Account account)
        {
            Text = "Subscription: " + account.Label; ClientSize = new Size(510, 235); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var help = new Label { Text = "Enter a date from your billing page. It will be labelled Manual.\nCheck the date to save it, or uncheck it to show Unavailable.", Location = new Point(16, 16), Size = new Size(478, 46) };
            var dateLabel = new Label { Text = "Manual date and time (local):", Location = new Point(16, 76), AutoSize = true };
            var date = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "d-MMM-yyyy HH:mm", ShowCheckBox = true, ShowUpDown = true,
                MinDate = new DateTime(1970, 1, 2), MaxDate = new DateTime(9998, 12, 31), Location = new Point(225, 72), Width = 269, AccessibleName = "Manual subscription date and time in local timezone" };
            DateTime initial = account.ManualSubscriptionAt.HasValue ? Json.LocalTime(account.ManualSubscriptionAt.Value) : DateTime.Today;
            date.Value = initial < date.MinDate ? date.MinDate : initial > date.MaxDate ? date.MaxDate : initial;
            date.Checked = account.ManualSubscriptionAt.HasValue;
            var kindLabel = new Label { Text = "Date means:", Location = new Point(16, 116), AutoSize = true };
            var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(225, 112), Width = 269, AccessibleName = "Subscription renews or ends" };
            kind.Items.AddRange(new object[] { "Renews", "Ends" }); kind.SelectedIndex = account.ManualSubscriptionKind == "Ends" ? 1 : 0;
            kind.Enabled = date.Checked; date.ValueChanged += delegate { kind.Enabled = date.Checked; };
            var reminder = new Label { Text = "Dates do not advance automatically. A past date stays labelled Manual.", Location = new Point(16, 150), Size = new Size(478, 32) };
            var save = new Button { Text = "Save", Location = new Point(324, 194), Width = 80 };
            save.Click += delegate
            {
                if (date.Checked)
                {
                    var local = new DateTime(date.Value.Year, date.Value.Month, date.Value.Day, date.Value.Hour, date.Value.Minute, 0, DateTimeKind.Unspecified);
                    if (TimeZoneInfo.Local.IsInvalidTime(local)) { MessageBox.Show(this, "This local time does not exist because the clocks change. Choose another time."); return; }
                    SubscriptionAt = (long)(TimeZoneInfo.ConvertTimeToUtc(local) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    SubscriptionKind = kind.Text;
                }
                else { SubscriptionAt = null; SubscriptionKind = null; }
                DialogResult = DialogResult.OK;
            };
            var cancel = new Button { Text = "Cancel", Location = new Point(414, 194), Width = 80, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { help, dateLabel, date, kindLabel, kind, reminder, save, cancel }); AcceptButton = save; CancelButton = cancel;
            Theme.Apply(this);
        }
    }
    internal sealed class SettingsForm : Form
    {
        private readonly TextBox path = new TextBox();
        private readonly CheckBox startup = new CheckBox();
        private readonly CheckBox dark = new CheckBox();
        public string Executable { get { return path.Text.Trim().Trim('"'); } }
        public bool StartupEnabled { get { return startup.Checked; } }
        public bool DarkEnabled { get { return dark.Checked; } }
        public SettingsForm(Settings settings, string root)
        {
            Text = "Monitor settings"; ClientSize = new Size(610, 340); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var label = new Label { Text = "Native Codex executable (codex.exe)", Location = new Point(16, 16), AutoSize = true };
            path.Text = settings.CodexPath; path.SetBounds(16, 43, 485, 24); path.AccessibleName = "Codex executable path";
            var browse = new Button { Text = "Browse...", Location = new Point(510, 41), Width = 85 };
            browse.Click += delegate { using (var picker = new OpenFileDialog { Filter = "Codex executable|*.exe", CheckFileExists = true }) if (picker.ShowDialog(this) == DialogResult.OK) path.Text = picker.FileName; };
            startup.Text = "Start with Windows (in the tray)"; startup.Checked = settings.StartWithWindows; startup.SetBounds(16, 88, 540, 26);
            dark.Text = "Dark theme"; dark.Checked = settings.DarkTheme; dark.SetBounds(16, 119, 540, 26);
            var help = new Label { Text = "Local data: " + root + "\n\nKeep the portable executable in a stable location. Move it, then run it once to update startup. Remove all accounts before deleting the app.", Location = new Point(16, 165), Size = new Size(575, 120) };
            var save = new Button { Text = "Save", Location = new Point(420, 300), Width = 80 };
            save.Click += delegate
            {
                if (!string.IsNullOrEmpty(Executable) && (!File.Exists(Executable) || !string.Equals(Path.GetExtension(Executable), ".exe", StringComparison.OrdinalIgnoreCase)))
                { MessageBox.Show(this, "Choose an existing native codex.exe, not a .cmd launcher."); return; }
                DialogResult = DialogResult.OK;
            };
            var cancel = new Button { Text = "Cancel", Location = new Point(510, 300), Width = 85, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { label, path, browse, startup, dark, help, save, cancel }); AcceptButton = save; CancelButton = cancel;
            Theme.Apply(this);
        }
    }

    internal sealed class HistoryForm : Form
    {
        private readonly Func<Task<ServiceUsage>> loadService;
        private readonly Label serviceStatus = new Label { Name = "historyStatus", Dock = DockStyle.Fill, Padding = new Padding(4, 8, 4, 4) };
        private readonly DataGridView daily = new DataGridView { Name = "dailyUsage", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        private readonly DailyTokenChart chart = new DailyTokenChart { Name = "tokenChart", Dock = DockStyle.Fill };
        private readonly Button refresh = new Button { Name = "historyRefresh", Text = "Refresh history", AutoSize = true };
        private readonly ComboBox view = new ComboBox { Name = "historyView", DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, AccessibleName = "History view" };
        private readonly ComboBox range = new ComboBox { Name = "historyRange", DropDownStyle = ComboBoxStyle.DropDownList, Width = 145, AccessibleName = "History time range" };
        private ServiceUsage usage;
        private DateTime updatedAt;
        private bool loading;
        public HistoryForm(Account account, Func<Task<ServiceUsage>> loader)
        {
            loadService = loader;
            Text = account.Label + " - daily token history";
            Font = new Font("Segoe UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(820, 590); MinimumSize = new Size(660, 450); StartPosition = FormStartPosition.CenterParent;
            daily.AccessibleName = "Exact daily token counts reported by OpenAI";
            daily.Columns.Add("date", "Date (reported by OpenAI)"); daily.Columns.Add("tokens", "Tokens");
            var body = new Panel { Dock = DockStyle.Fill }; body.Controls.Add(daily); body.Controls.Add(chart);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill };
            view.Items.AddRange(new object[] { "Chart", "Table" });
            range.Items.AddRange(new object[] { "7 days", "30 days", "All available" });
            controls.Controls.Add(view); controls.Controls.Add(range); controls.Controls.Add(refresh);
            var legend = new Label { Dock = DockStyle.Fill, Padding = new Padding(4, 8, 4, 0),
                Text = "Daily tokens, not remaining quota. Zero = baseline mark; x = unavailable; blank dates = not reported." };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.Controls.Add(controls, 0, 0); layout.Controls.Add(serviceStatus, 0, 1); layout.Controls.Add(body, 0, 2); layout.Controls.Add(legend, 0, 3);
            Controls.Add(layout);
            view.SelectedIndexChanged += delegate
            {
                bool showChart = view.SelectedIndex == 0; chart.Visible = showChart; daily.Visible = !showChart;
                if (showChart) chart.BringToFront(); else daily.BringToFront();
            };
            range.SelectedIndexChanged += delegate { RefreshView(); };
            view.SelectedIndex = 0; range.SelectedIndex = 1;
            refresh.Click += async delegate { await ReadService(); };
            Shown += async delegate { await ReadService(); };
            Theme.Apply(this);
        }
        private static string Tokens(long? count) { return count.HasValue ? count.Value.ToString("N0", CultureInfo.CurrentCulture) : "Unavailable"; }
        private void RefreshView()
        {
            int days = new[] { 7, 30, 0 }[Math.Max(0, range.SelectedIndex)];
            var today = DateTime.UtcNow.Date;
            var points = usage == null ? null : usage.InRange(days, today);
            daily.Rows.Clear();
            if (points != null)
                foreach (var day in points) daily.Rows.Add(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Tokens(day.Tokens));
            chart.Points = points ?? new List<DailyTokens>();
            chart.Start = days == 0 ? (chart.Points.Count == 0 ? today : chart.Points.Min(d => d.Date)) : today.AddDays(1 - days);
            chart.End = days == 0 && chart.Points.Count > 0 ? chart.Points.Max(d => d.Date) : today;
            chart.EmptyMessage = loading ? "Reading usage history from OpenAI..." : points == null ? "OpenAI daily history unavailable." :
                usage.Days.Count == 0 ? "OpenAI returned no daily buckets." : "No reported days in this period.";
            if (usage != null)
                serviceStatus.Text = "Lifetime tokens: " + Tokens(usage.LifetimeTokens) + "    Peak daily tokens: " + Tokens(usage.PeakDailyTokens) + "\n" +
                    (points == null || points.Count == 0 ? chart.EmptyMessage : "Dates are reported by OpenAI. 7/30-day filters use UTC dates.") +
                    "\nUpdated " + updatedAt.ToString("d-MMM HH:mm");
            chart.Invalidate();
        }
        private async Task ReadService()
        {
            if (loading || IsDisposed) return;
            loading = true; refresh.Enabled = false; usage = null;
            serviceStatus.Text = "Reading usage history from OpenAI..."; RefreshView();
            try
            {
                var result = await loadService();
                if (IsDisposed) return;
                if (result == null) throw new InvalidDataException("OpenAI returned no usage response.");
                usage = result; updatedAt = DateTime.Now;
            }
            catch (Exception error)
            {
                if (!IsDisposed) serviceStatus.Text = error.Message.Contains("(-32601)") ? "This Codex version does not support account/usage/read. Choose a compatible newer codex.exe in Settings." :
                    "OpenAI history unavailable. " + error.Message + "\nNo estimated history has been substituted.";
            }
            finally { loading = false; if (!IsDisposed) { refresh.Enabled = true; RefreshView(); } }
        }
    }
    internal sealed class DailyTokenChart : Panel
    {
        public List<DailyTokens> Points = new List<DailyTokens>();
        public DateTime Start, End;
        public string EmptyMessage = "OpenAI daily history unavailable.";
        public DailyTokenChart()
        {
            DoubleBuffered = true; BackColor = Theme.Surface;
            AccessibleName = "Daily tokens bar chart"; AccessibleDescription = "Use the Table view for exact counts. Missing dates are not zero.";
            ResizeRedraw = true;
        }
        private static string AxisLabel(double value)
        {
            return value >= 1e12 ? value.ToString("0.#E+0", CultureInfo.InvariantCulture) :
                value >= 1e9 ? (value / 1e9).ToString("0.#") + "B" : value >= 1e6 ? (value / 1e6).ToString("0.#") + "M" :
                value >= 1000 ? (value / 1000).ToString("0.#") + "K" : value.ToString("0.#");
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            if (Width < 160 || Height < 120 || End < Start) return;
            var graphics = args.Graphics;
            float scale = Font.Height / 15F;
            var area = new RectangleF(68 * scale, 28 * scale, Width - 90 * scale, Height - 70 * scale);
            if (area.Width <= 0 || area.Height <= 0) return;
            if (Points.Count == 0)
            {
                TextRenderer.DrawText(graphics, EmptyMessage, Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            double maximum = Math.Max(4, Points.Where(d => d.Tokens.HasValue).Select(d => (double)d.Tokens.Value).DefaultIfEmpty(0).Max());
            double span = (End - Start).TotalDays + 1;
            using (var gridPen = new Pen(Color.FromArgb(120, Theme.Border)))
            using (var zeroPen = new Pen(Theme.Accent, 2))
            using (var unknownPen = new Pen(Theme.Muted))
            using (var bar = new SolidBrush(Theme.Accent))
            using (var labels = new SolidBrush(Theme.Muted))
            {
                graphics.DrawString("Tokens", Font, labels, 4, 0);
                for (int level = 0; level <= 4; level++)
                {
                    float y = area.Bottom - area.Height * level / 4;
                    graphics.DrawLine(gridPen, area.Left, y, area.Right, y);
                    string label = AxisLabel(maximum * level / 4);
                    graphics.DrawString(label, Font, labels, Math.Max(0, area.Left - graphics.MeasureString(label, Font).Width - 8), y - Font.Height / 2F);
                }
                foreach (var point in Points)
                {
                    if (point.Date < Start || point.Date > End) continue;
                    float x = area.Left + area.Width * (float)(((point.Date - Start).TotalDays + 0.5) / span);
                    float width = Math.Max(1, Math.Min(28 * scale, area.Width / (float)span * 0.65F));
                    if (!point.Tokens.HasValue)
                    {
                        graphics.DrawLine(unknownPen, x - 3, area.Bottom - 7, x + 3, area.Bottom - 1);
                        graphics.DrawLine(unknownPen, x - 3, area.Bottom - 1, x + 3, area.Bottom - 7);
                    }
                    else if (point.Tokens.Value == 0) graphics.DrawLine(zeroPen, x - width / 2, area.Bottom - 1, x + width / 2, area.Bottom - 1);
                    else
                    {
                        float height = Math.Max(1, area.Height * (float)(point.Tokens.Value / maximum));
                        graphics.FillRectangle(bar, x - width / 2, area.Bottom - height, width, height);
                    }
                }
                var ticks = new[] { Start, Start.AddDays(Math.Floor((span - 1) / 2)), End }.Distinct();
                foreach (var date in ticks)
                {
                    string label = date.ToString(Start.Year == End.Year ? "d MMM" : "d MMM yy", CultureInfo.InvariantCulture);
                    float width = graphics.MeasureString(label, Font).Width;
                    float x = area.Left + area.Width * (float)(((date - Start).TotalDays + 0.5) / span);
                    graphics.DrawString(label, Font, labels, Math.Max(area.Left, Math.Min(area.Right - width, x - width / 2)), area.Bottom + 10);
                }
            }
        }
    }
}
