using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WallpaperControl
{
    // Settings dialog Choices members. See README.md in this directory for the code map.
    internal sealed partial class SettingsForm
    {
        /// <summary>
        /// Pairs a selector label with its stable settings value.
        /// </summary>
        private sealed class Choice
        {
            public string Text { get; }
            public uint Value { get; }

            /// <summary>
            /// Stores the display label and its stable selection value.
            /// </summary>
            /// <param name="text">The text to display, format, or parse.</param>
            /// <param name="value">The underlying key or modifier value represented by this choice.</param>
            public Choice(
                string text,
                uint value)
            {
                Text = text;
                Value = value;
            }

            /// <summary>
            /// Returns the label displayed by the selector.
            /// </summary>
            /// <returns>The display label shown in the selection control.</returns>
            public override string ToString() =>
                Text;
        }

        /// <summary>
        /// Pairs a selector label with its stable settings value.
        /// </summary>
        private sealed class LanguageChoice
        {
            public string Text { get; }
            public string Code { get; }

            /// <summary>
            /// Stores the display label and its stable selection value.
            /// </summary>
            /// <param name="text">The text to display, format, or parse.</param>
            /// <param name="code">The culture code represented by this language choice.</param>
            public LanguageChoice(
                string text,
                string code)
            {
                Text = text;
                Code = code;
            }

            /// <summary>
            /// Returns the label displayed by the selector.
            /// </summary>
            /// <returns>The display label shown in the selection control.</returns>
            public override string ToString() =>
                Text;
        }

        /// <summary>
        /// Pairs a selector label with its stable settings value.
        /// </summary>
        private sealed class ThemeChoice
        {
            public string Text { get; }
            public string Mode { get; }

            /// <summary>
            /// Stores the display label and its stable selection value.
            /// </summary>
            /// <param name="text">The text to display, format, or parse.</param>
            /// <param name="mode">The theme mode represented by this choice.</param>
            public ThemeChoice(
                string text,
                string mode)
            {
                Text = text;
                Mode = mode;
            }

            /// <summary>
            /// Returns the label displayed by the selector.
            /// </summary>
            /// <returns>The display label shown in the selection control.</returns>
            public override string ToString() =>
                Text;
        }


    }
}
