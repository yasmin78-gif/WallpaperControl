using System;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class PreviewForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;

        protected override bool ShowWithoutActivation =>
            true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters =
                    base.CreateParams;

                parameters.ExStyle |=
                    WS_EX_NOACTIVATE;

                return parameters;
            }
        }
    }
}
