using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Launcher
{
    /// <summary>Barra de tarefas: botão Iniciar (menu por categoria), programas abertos e relógio.</summary>
    internal sealed class TaskbarForm : Form
    {
        public const int BarHeight = 42;

        private static readonly Color BarColor = Color.FromArgb(28, 32, 44);
        private static readonly Color AccentColor = Color.FromArgb(0, 102, 204);

        private readonly ProgramRunner _runner;
        private readonly List<ProgramItem> _programs;
        private readonly bool _preview;
        private readonly Button _startButton;
        private readonly FlowLayoutPanel _tasksPanel;
        private readonly Label _clock;
        private readonly Timer _timer;
        private readonly ContextMenuStrip _menu;
        private readonly List<KeyValuePair<Process, Button>> _tasks = new List<KeyValuePair<Process, Button>>();

        public TaskbarForm(ProgramRunner runner, List<ProgramItem> programs, bool preview)
        {
            _runner = runner;
            _programs = programs;
            _preview = preview;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = BarColor;
            Font = SystemFonts.MessageBoxFont;

            _tasksPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(6, 5, 0, 0),
                BackColor = BarColor,
            };

            _clock = new Label
            {
                Dock = DockStyle.Right,
                Width = 70,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = BarColor,
            };

            _startButton = new Button
            {
                Dock = DockStyle.Left,
                Width = 104,
                Text = "Iniciar",
                Font = new Font(Font, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = AccentColor,
                TabStop = false,
            };
            _startButton.FlatAppearance.BorderSize = 0;
            _startButton.Click += (s, e) => ShowStartMenu();

            // Ordem do designer: o controle Fill entra primeiro.
            Controls.Add(_tasksPanel);
            Controls.Add(_clock);
            Controls.Add(_startButton);

            _menu = new ContextMenuStrip { Font = Font, ShowImageMargin = true };

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
            Tick();
        }

        /// <summary>Posiciona a barra no rodapé da área informada.</summary>
        public void DockToBottom(Rectangle area)
        {
            Bounds = new Rectangle(area.Left, area.Bottom - BarHeight, area.Width, BarHeight);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void AddTask(Process process, string name)
        {
            if (process == null)
            {
                return;
            }

            var button = new Button
            {
                Width = 170,
                Height = BarHeight - 10,
                Text = Shorten(name),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(52, 60, 80),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 4, 0),
                TabStop = false,
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (s, e) =>
            {
                try
                {
                    process.Refresh();
                    Native.BringToFront(process.MainWindowHandle);
                }
                catch (InvalidOperationException)
                {
                    // o processo acabou de terminar; a próxima passada do timer remove o botão
                }
            };
            _tasks.Add(new KeyValuePair<Process, Button>(process, button));
            _tasksPanel.Controls.Add(button);
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= 22 ? text : text.Substring(0, 21) + "…";
        }

        private void Tick()
        {
            _clock.Text = DateTime.Now.ToString("HH:mm");

            for (var i = _tasks.Count - 1; i >= 0; i--)
            {
                var process = _tasks[i].Key;
                var button = _tasks[i].Value;
                bool exited;
                try
                {
                    exited = process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    exited = true;
                }

                if (exited)
                {
                    _tasksPanel.Controls.Remove(button);
                    button.Dispose();
                    process.Dispose();
                    _tasks.RemoveAt(i);
                }
            }
        }

        // ---- menu Iniciar -------------------------------------------------------------------------------

        private void ShowStartMenu()
        {
            BuildMenu();
            _menu.Show(_startButton, Point.Empty, ToolStripDropDownDirection.AboveRight);
        }

        private void BuildMenu()
        {
            _menu.Items.Clear();
            var groups = ManifestReader.GroupForStartMenu(_programs);

            if (groups.Count == 0)
            {
                var empty = new ToolStripMenuItem("(nenhum programa no menu)") { Enabled = false };
                _menu.Items.Add(empty);
            }
            else if (groups.Count == 1)
            {
                foreach (var program in groups[0].Value)
                {
                    _menu.Items.Add(CreateProgramItem(program));
                }
            }
            else
            {
                foreach (var group in groups)
                {
                    var folder = new ToolStripMenuItem(group.Key);
                    foreach (var program in group.Value)
                    {
                        folder.DropDownItems.Add(CreateProgramItem(program));
                    }

                    _menu.Items.Add(folder);
                }
            }

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(CreateToolItem("Prompt de comando", SystemFile("cmd.exe"), string.Empty));
            var powerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(powerShell))
            {
                _menu.Items.Add(CreateToolItem("PowerShell", powerShell, string.Empty));
            }

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(CreatePowerItem("Reiniciar", "reboot"));
            _menu.Items.Add(CreatePowerItem("Desligar", "shutdown"));
        }

        private static string SystemFile(string name)
        {
            return Path.Combine(Environment.SystemDirectory, name);
        }

        private ToolStripMenuItem CreateProgramItem(ProgramItem program)
        {
            var item = new ToolStripMenuItem(program.Name)
            {
                Image = ProgramRunner.LoadIcon(_runner.ExePath(program), 16),
                ToolTipText = program.Exe,
            };
            item.Click += (s, e) => Launch(program);
            return item;
        }

        private ToolStripMenuItem CreateToolItem(string text, string file, string arguments)
        {
            var item = new ToolStripMenuItem(text) { Image = ProgramRunner.LoadIcon(file, 16) };
            item.Click += (s, e) =>
            {
                var process = _runner.StartFile(file, arguments, Path.GetPathRoot(Environment.SystemDirectory), text);
                AddTask(process, text);
            };
            return item;
        }

        private ToolStripMenuItem CreatePowerItem(string text, string wpeutilCommand)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) =>
            {
                if (_preview)
                {
                    MessageBox.Show(this, "(modo de teste) \"" + text + "\" só funciona dentro do WinPE.", "WinPE Launcher");
                    return;
                }

                var answer = MessageBox.Show(this, text + " o computador agora?", "WinPE Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(SystemFile("wpeutil.exe"), wpeutilCommand) { UseShellExecute = false, CreateNoWindow = true });
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                {
                    AppLog.Write("wpeutil " + wpeutilCommand + " falhou: " + ex);
                    ProgramRunner.Error("Não foi possível executar o comando (wpeutil).");
                }
            };
            return item;
        }

        /// <summary>Inicia um programa e põe o botão dele na barra.</summary>
        public void Launch(ProgramItem program)
        {
            AddTask(_runner.Start(program), program.Name);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _menu.Dispose();
                foreach (var task in _tasks.ToList())
                {
                    task.Key.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
