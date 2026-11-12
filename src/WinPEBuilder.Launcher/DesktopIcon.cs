using System;
using System.Drawing;
using System.Windows.Forms;
using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Launcher
{
    /// <summary>
    /// Ícone da área de trabalho desenhado por nós (fundo transparente), para aparecer sobre o papel de parede.
    /// O ListView do Windows não permite fundo transparente, por isso não é usado aqui.
    /// </summary>
    internal sealed class DesktopIcon : Control
    {
        private static readonly TextFormatFlags LabelFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top
            | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        private readonly Image _image;
        private readonly string _label;
        private bool _selected;

        public DesktopIcon(Image image, string label, object payload)
        {
            _image = image;
            _label = label;
            Payload = payload;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            BackColor = Color.Transparent;
            Size = new Size(ManifestReader.CellSize - 4, ManifestReader.CellSize - 4);
            TabStop = false;
        }

        /// <summary>O que o ícone representa: um ProgramItem ou um DesktopFolder.</summary>
        public object Payload { get; private set; }

        public event EventHandler Chosen;

        public event EventHandler Activated;

        public bool Selected
        {
            get
            {
                return _selected;
            }

            set
            {
                if (_selected != value)
                {
                    _selected = value;
                    Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_selected)
            {
                using (var fill = new SolidBrush(Color.FromArgb(85, 255, 255, 255)))
                using (var edge = new Pen(Color.FromArgb(153, 255, 255, 255)))
                {
                    e.Graphics.FillRectangle(fill, 0, 0, Width - 1, Height - 1);
                    e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
                }
            }

            e.Graphics.DrawImage(_image, (Width - 32) / 2, 6, 32, 32);

            var area = new Rectangle(2, 42, Width - 4, Height - 44);
            // sombra preta atrás do texto branco: legível sobre qualquer imagem
            TextRenderer.DrawText(e.Graphics, _label, Font, new Rectangle(area.X + 1, area.Y + 1, area.Width, area.Height), Color.Black, LabelFlags);
            TextRenderer.DrawText(e.Graphics, _label, Font, area, Color.White, LabelFlags);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                var handler = Chosen;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            Activate();
        }

        public void Activate()
        {
            var handler = Activated;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                _image.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
