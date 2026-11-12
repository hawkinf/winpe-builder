using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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

    /// <summary>Janela que mostra os programas de uma pasta da área de trabalho.</summary>
    internal sealed class FolderForm : Form
    {
        public FolderForm(DesktopFolder folder, ProgramRunner runner, Action<ProgramItem> launch)
        {
            Text = folder.Name;
            Icon = SystemIcons.Application;
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 380);
            MinimumSize = new Size(300, 200);
            ShowInTaskbar = false;

            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                BorderStyle = BorderStyle.None,
                MultiSelect = false,
                ShowItemToolTips = true,
                Activation = ItemActivation.Standard,
            };

            var images = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
            list.LargeImageList = images;
            foreach (var program in folder.Programs)
            {
                images.Images.Add(ProgramRunner.LoadIcon(runner.ExePath(program), 32));
                list.Items.Add(new ListViewItem(program.Name, images.Images.Count - 1) { Tag = program, ToolTipText = program.Exe });
            }

            list.ItemActivate += (s, e) =>
            {
                if (list.SelectedItems.Count > 0)
                {
                    launch((ProgramItem)list.SelectedItems[0].Tag);
                }
            };

            Controls.Add(list);
        }
    }
}
