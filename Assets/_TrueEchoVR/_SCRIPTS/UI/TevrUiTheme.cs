using UnityEngine;

namespace TEVR
{
    /// <summary>
    /// Central dark-glass + cyan-glow palette for the TrueEchoVR MR user interface.
    /// Exposes the theme colors as static readonly fields so UI code and editor
    /// scripts share a single source of truth.
    /// </summary>
    public static class TevrUiTheme
    {
        /// <summary>Panel background: #0A0E14 @ alpha 0.78 (10,14,20,199).</summary>
        public static readonly Color PanelBg = new Color(10f / 255f, 14f / 255f, 20f / 255f, 199f / 255f);

        /// <summary>Cyan accent / border glow: #22D3EE (34,211,238,255).</summary>
        public static readonly Color Accent = new Color(34f / 255f, 211f / 255f, 238f / 255f, 1f);

        /// <summary>Button normal: #0E1620 @ alpha 0.88.</summary>
        public static readonly Color ButtonNormal = new Color(14f / 255f, 22f / 255f, 32f / 255f, 0.88f);

        /// <summary>Button hover / highlighted: #123040 with cyan tint.</summary>
        public static readonly Color ButtonHover = new Color(18f / 255f, 48f / 255f, 64f / 255f, 1f);

        /// <summary>Button pressed: cyan #22D3EE @ alpha 0.35.</summary>
        public static readonly Color ButtonPressed = new Color(34f / 255f, 211f / 255f, 238f / 255f, 0.35f);

        /// <summary>Primary body text: #E6F6FF.</summary>
        public static readonly Color TextPrimary = new Color(230f / 255f, 246f / 255f, 255f / 255f, 1f);

        /// <summary>Accent / header text: #22D3EE.</summary>
        public static readonly Color TextAccent = new Color(34f / 255f, 211f / 255f, 238f / 255f, 1f);

        /// <summary>Disabled state: #1A2230 @ alpha 0.5.</summary>
        public static readonly Color Disabled = new Color(26f / 255f, 34f / 255f, 48f / 255f, 0.5f);

        /// <summary>
        /// Parse a hex color string (e.g. "#22D3EE" or "22D3EE") and apply an alpha override.
        /// </summary>
        public static Color FromHex(string hex, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return new Color(1f, 1f, 1f, alpha);
            }

            if (hex[0] == '#')
            {
                hex = hex.Substring(1);
            }

            if (hex.Length < 6)
            {
                return new Color(1f, 1f, 1f, alpha);
            }

            float r = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float g = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            float b = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f;
            return new Color(r, g, b, alpha);
        }
    }
}
