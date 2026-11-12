using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Launcher
{
    /// <summary>
    /// Área de trabalho: tela cheia atrás de tudo, com papel de parede (cor e/ou imagem) e os ícones dos programas
    /// marcados para o desktop, posicionados na grade escolhida no editor visual.
    /// </summary>
    internal sealed class DesktopForm : Form
    {
        private readonly bool _preview;
        private readonly TaskbarForm _taskbar;
        private readonly ProgramRunner _runner;
        private readonly List<LayoutEntry> _layout;
        private readonly List<KeyValuePair<string, DesktopIcon>> _icons = new List<KeyValuePair<string, DesktopIcon>>();
        private readonly ToolTip _tips = new ToolTip();
        private readonly Color _color;
        private readonly string _wallpaperMode;
        private Image _wallpaperImage;
        private Bitmap _wallpaperBitmap;
        private DesktopIcon _selected;
        private Rectangle _originalWorkArea;

        public DesktopForm(ProgramRunner runner, List<ProgramItem> programs, List<LayoutEntry> layout, List<string> menuOrder,
            WallpaperInfo wallpaper, bool preview, string notice)
        {
            _preview = preview;
            _runner = runner;
            _layout = layout;
            _taskbar = new TaskbarForm(runner, programs, menuOrder, preview);

            _color = WallpaperGeometry.ParseColor(wallpaper == null ? null : wallpaper.Color);
            _wallpaperMode = WallpaperGeometry.NormalizeMode(wallpaper == null ? null : wallpaper.Mode);
            LoadWallpaper(wallpaper);

            Text = "WinPE Launcher (teste)";
            Icon = SystemIcons.Application;
            BackColor = _color;
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.Manual;
            ShowInStartTaskbar(preview);
            DoubleBuffered = true;
            ResizeRedraw = true;
            KeyPreview = true;

            // Pastas primeiro (como no Explorer), depois os programas soltos.
            foreach (var group in ManifestReader.DesktopFolders(programs))
            {
                var folder = new DesktopFolder(group.Key, group.Value);
                AddIcon(ManifestReader.FolderKey(group.Key), FolderIcon.Create(32), group.Key, folder, group.Value.Count + " programa(s)");
            }

            foreach (var program in ManifestReader.ForDesktop(programs))
            {
                AddIcon(ManifestReader.ProgramKey(program), ProgramRunner.LoadIcon(runner.ExePath(program), 32), program.Name, program, program.Exe);
            }

            var message = notice;
            if (string.IsNullOrEmpty(message) && _icons.Count == 0)
            {
                message = "Nenhum programa na área de trabalho. Use o menu Iniciar.";
            }

            if (!string.IsNullOrEmpty(message))
            {
                Controls.Add(new Label
                {
                    Dock = DockStyle.Bottom,
                    Height = 40,
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Text = message,
                });
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

        private void ShowInStartTaskbar(bool preview)
        {
            ShowInTaskbar = preview;
        }

        // ---- papel de parede ------------------------------------------------------------------------------

        private void LoadWallpaper(WallpaperInfo wallpaper)
        {
            if (wallpaper == null || string.IsNullOrEmpty(wallpaper.File))
            {
                return;
            }

            var path = ManifestReader.ResolveRelative(_runner.FilesRoot, wallpaper.File);
            if (path == null || !File.Exists(path))
            {
                AppLog.Write("papel de parede não encontrado: " + wallpaper.File);
                return;
            }

            try
            {
                // copia para um Bitmap próprio: não deixa o arquivo (na mídia) aberto
                using (var stream = File.OpenRead(path))
                using (var loaded = Image.FromStream(stream))
                {
                    _wallpaperImage = new Bitmap(loaded);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is OutOfMemoryException || ex is UnauthorizedAccessException)
            {
                // GDI+ lança OutOfMemoryException para arquivos que não são imagem
                AppLog.Write("papel de parede ilegível (" + wallpaper.File + "): " + ex.Message);
            }
        }

        /// <summary>Desenha o papel de parede já no tamanho da tela (uma vez por tamanho, não a cada repintura).</summary>
        private void RebuildWallpaperBitmap()
        {
            var size = ClientSize;
            if (_wallpaperImage == null || size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            if (_wallpaperBitmap != null && _wallpaperBitmap.Size == size)
            {
                return;
            }

            var bitmap = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(_color);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                foreach (var rect in WallpaperGeometry.Compute(_wallpaperMode, _wallpaperImage.Width, _wallpaperImage.Height, size.Width, size.Height))
                {
                    g.DrawImage(_wallpaperImage, rect);
                }
            }

            if (_wallpaperBitmap != null)
            {
                _wallpaperBitmap.Dispose();
            }

            _wallpaperBitmap = bitmap;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            RebuildWallpaperBitmap();
            if (_wallpaperBitmap != null)
            {
                e.Graphics.DrawImageUnscaled(_wallpaperBitmap, 0, 0);
            }
            else
            {
                base.OnPaintBackground(e);
            }
        }

        // ---- ícones -----------------------------------------------------------------------------------------

        private void AddIcon(string key, Image image, string label, object payload, string tooltip)
        {
            var icon = new DesktopIcon(image, label, payload);
            icon.Font = Font;
            icon.Chosen += (s, e) => Select((DesktopIcon)s);
            icon.Activated += (s, e) => ActivateIcon((DesktopIcon)s);
            _tips.SetToolTip(icon, tooltip);
            Controls.Add(icon);
            _icons.Add(new KeyValuePair<string, DesktopIcon>(key, icon));
        }

        private void Select(DesktopIcon icon)
        {
            if (_selected != null && !ReferenceEquals(_selected, icon))
            {
                _selected.Selected = false;
            }

            _selected = icon;
            if (icon != null)
            {
                icon.Selected = true;
            }
        }

        private void ActivateIcon(DesktopIcon icon)
        {
            var program = icon.Payload as ProgramItem;
            if (program != null)
            {
                _taskbar.Launch(program);
                return;
            }

            OpenFolder((DesktopFolder)icon.Payload, _runner);
        }

        /// <summary>
        /// Posiciona os ícones na grade do editor visual (coluna x linha de 80 px). O que não tiver posição, ou cair fora da
        /// tela, vai para a primeira célula livre: nunca esconde um programa.
        /// </summary>
        private void LayoutIcons()
        {
            var cell = ManifestReader.CellSize;
            var keys = new List<string>();
            foreach (var pair in _icons)
            {
                keys.Add(pair.Key);
            }

            var cells = ManifestReader.PlaceIcons(keys, _layout, ClientSize.Width / cell, ClientSize.Height / cell);
            foreach (var pair in _icons)
            {
                var at = cells[pair.Key];
                pair.Value.Location = new Point((at.X * cell) + 2, (at.Y * cell) + 2);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutIcons();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Select(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter && _selected != null)
            {
                _selected.Activate();
                e.Handled = true;
            }
        }

        private readonly Dictionary<string, FolderForm> _openFolders = new Dictionary<string, FolderForm>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>Abre (ou traz para frente) a janela de uma pasta da área de trabalho.</summary>
        private void OpenFolder(DesktopFolder folder, ProgramRunner runner)
        {
            FolderForm window;
            if (_openFolders.TryGetValue(folder.Name, out window) && !window.IsDisposed)
            {
                window.Activate();
                return;
            }

            window = new FolderForm(folder, runner, p => _taskbar.Launch(p));
            window.FormClosed += (s, e) => _openFolders.Remove(folder.Name);
            _openFolders[folder.Name] = window;
            window.Show();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutIcons();
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
                _tips.Dispose();
                if (_wallpaperImage != null)
                {
                    _wallpaperImage.Dispose();
                }

                if (_wallpaperBitmap != null)
                {
                    _wallpaperBitmap.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
