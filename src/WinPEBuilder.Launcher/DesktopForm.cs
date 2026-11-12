using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Launcher
{
    /// <summary>Área de trabalho: tela cheia atrás de tudo, com os ícones dos programas marcados para o desktop.</summary>
    internal sealed class DesktopForm : Form
    {
        private static readonly Color DesktopColor = Color.FromArgb(18, 70, 120);

        private readonly bool _preview;
        private readonly TaskbarForm _taskbar;
        private readonly ListView _icons;
        private Rectangle _originalWorkArea;

        public DesktopForm(ProgramRunner runner, List<ProgramItem> programs, bool preview, string notice)
        {
            _preview = preview;
            _taskbar = new TaskbarForm(runner, programs, preview);

            Text = "WinPE Launcher (teste)";
            Icon = SystemIcons.Application;
            BackColor = DesktopColor;
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = preview;

            _icons = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                BackColor = DesktopColor,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                MultiSelect = false,
                HideSelection = false,
                ShowItemToolTips = true,
                Activation = ItemActivation.Standard,
            };

            var images = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
            _icons.LargeImageList = images;
            foreach (var program in ManifestReader.ForDesktop(programs))
            {
                images.Images.Add(ProgramRunner.LoadIcon(runner.ExePath(program), 32));
                _icons.Items.Add(new ListViewItem(program.Name, images.Images.Count - 1) { Tag = program, ToolTipText = program.Exe });
            }

            _icons.ItemActivate += (s, e) =>
            {
                if (_icons.SelectedItems.Count > 0)
                {
                    _taskbar.Launch((ProgramItem)_icons.SelectedItems[0].Tag);
                }
            };

            Controls.Add(_icons);

            var message = notice;
            if (string.IsNullOrEmpty(message) && _icons.Items.Count == 0)
            {
                message = "Nenhum programa na área de trabalho. Use o menu Iniciar.";
            }

            if (!string.IsNullOrEmpty(message))
            {
                var label = new Label
                {
                    Dock = DockStyle.Bottom,
                    Height = 40,
                    ForeColor = Color.White,
                    BackColor = DesktopColor,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Text = message,
                };
                Controls.Add(label);
            }

            if (preview)
            {
                // Modo de teste: janela normal sobre a área útil; não mexe na área de trabalho do Windows.
                FormBorderStyle = FormBorderStyle.Sizable;
                var area = Screen.PrimaryScreen.WorkingArea;
                Bounds = new Rectangle(area.Left + 60, area.Top + 40, Math.Min(900, area.Width - 120), Math.Min(560, area.Height - TaskbarForm.BarHeight - 80));
                _taskbar.DockToBottom(area);
            }
            else
            {
                // WinPE: sem moldura, ocupa a tela toda menos a barra, e fica sempre atrás das outras janelas.
                FormBorderStyle = FormBorderStyle.None;
                var screen = Screen.PrimaryScreen.Bounds;
                _originalWorkArea = Native.GetWorkArea();
                var usable = new Rectangle(screen.Left, screen.Top, screen.Width, screen.Height - TaskbarForm.BarHeight);
                Bounds = usable;
                _taskbar.DockToBottom(screen);
                Native.SetWorkArea(usable);
                Activated += (s, e) => Native.SendToBottom(Handle);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _taskbar.Show();
            if (!_preview)
            {
                Native.SendToBottom(Handle);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Dentro do WinPE o launcher é o "shell": Alt+F4 não pode fechá-lo.
            if (!_preview && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!_preview && !_originalWorkArea.IsEmpty)
            {
                Native.SetWorkArea(_originalWorkArea);
            }

            _taskbar.Close();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _taskbar.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
