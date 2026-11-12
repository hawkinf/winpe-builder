using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Launcher
{
    /// <summary>Uma pasta da área de trabalho: nome + programas que ela contém.</summary>
    internal sealed class DesktopFolder
    {
        public DesktopFolder(string name, List<ProgramItem> programs)
        {
            Name = name;
            Programs = programs;
        }

        public string Name { get; private set; }

        public List<ProgramItem> Programs { get; private set; }
    }

    /// <summary>Ícone de pasta desenhado em código (não depende do shell, que não existe no WinPE).</summary>
    internal static class FolderIcon
    {
        public static Bitmap Create(int size)
        {
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size / 32f;
                using (var back = new SolidBrush(Color.FromArgb(222, 168, 40)))
                using (var front = new LinearGradientBrush(new RectangleF(0, 10 * s, size, 20 * s), Color.FromArgb(255, 214, 102), Color.FromArgb(240, 180, 50), 90f))
                using (var edge = new Pen(Color.FromArgb(150, 100, 10), Math.Max(1f, s)))
                {
                    g.FillRectangle(back, 2 * s, 6 * s, 12 * s, 6 * s);
                    g.FillRectangle(front, 2 * s, 10 * s, 28 * s, 18 * s);
                    g.DrawRectangle(edge, 2 * s, 10 * s, 28 * s, 18 * s);
                }
            }

            return bitmap;
        }
    }

    /// <summary>Destino de navegacao de um item da janela de pasta (subpasta ou "voltar").</summary>
    internal sealed class FolderNav
    {
        public FolderNav(string path)
        {
            Path = path;
        }

        public string Path { get; private set; }
    }

    /// <summary>Janela que mostra o conteudo de uma pasta da area de trabalho: subpastas (navegaveis) e programas.</summary>
    internal sealed class FolderForm : Form
    {
        private readonly ListView _list;
        private readonly ImageList _images;
        private readonly List<ProgramItem> _all;
        private readonly ProgramRunner _runner;
        private readonly Action<ProgramItem> _launch;
        private string _current;

        public FolderForm(DesktopFolder folder, ProgramRunner runner, Action<ProgramItem> launch)
        {
            _all = folder.Programs;
            _runner = runner;
            _launch = launch;
            _current = folder.Name;

            Icon = SystemIcons.Application;
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 380);
            MinimumSize = new Size(300, 200);
            ShowInTaskbar = false;

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                BorderStyle = BorderStyle.None,
                MultiSelect = false,
                ShowItemToolTips = true,
                Activation = ItemActivation.Standard,
            };
            _images = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
            _list.LargeImageList = _images;
            _list.ItemActivate += (s, e) => Activate(_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag : null);
            Controls.Add(_list);
            Populate();
        }

        private void Activate(object tag)
        {
            var program = tag as ProgramItem;
            if (program != null)
            {
                _launch(program);
                return;
            }

            var nav = tag as FolderNav;
            if (nav != null)
            {
                _current = nav.Path;
                Populate();
            }
        }

        private void Populate()
        {
            _list.Items.Clear();
            _images.Images.Clear();
            Text = string.Join(" > ", ManifestReader.FolderSegments(_current));
            var content = ManifestReader.FolderContents(_all, _current);

            if (ManifestReader.FolderSegments(_current).Count > 1)
            {
                _images.Images.Add(FolderIcon.Create(32));
                _list.Items.Add(new ListViewItem("..", _images.Images.Count - 1)
                {
                    Tag = new FolderNav(ManifestReader.FolderParent(_current)),
                    ToolTipText = "Voltar para a pasta de cima",
                });
            }

            foreach (var sub in content.Subfolders)
            {
                _images.Images.Add(FolderIcon.Create(32));
                var leaf = ManifestReader.FolderSegments(sub).Last();
                _list.Items.Add(new ListViewItem(leaf, _images.Images.Count - 1) { Tag = new FolderNav(sub), ToolTipText = "Pasta - duplo clique abre" });
            }

            foreach (var program in content.Programs)
            {
                _images.Images.Add(ProgramRunner.LoadIcon(_runner.ExePath(program), 32));
                _list.Items.Add(new ListViewItem(program.Name, _images.Images.Count - 1) { Tag = program, ToolTipText = program.Exe });
            }
        }
    }
}
