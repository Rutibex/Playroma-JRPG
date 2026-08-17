using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class EffectsPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly Func<IReadOnlyCollection<int>> selectedFrameIndexes;
    private readonly Func<Rgba32> foreground;
    private readonly Func<Rgba32> background;
    private readonly ComboBox effectBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox scopeBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown amount1 = new() { Minimum = -200, Maximum = 256, Value = 0, DecimalPlaces = 1, Increment = 1, Dock = DockStyle.Fill };
    private readonly NumericUpDown amount2 = new() { Minimum = -200, Maximum = 200, Value = 0, DecimalPlaces = 1, Increment = 1, Dock = DockStyle.Fill };
    private readonly CheckBox selectionOnly = new() { Text = "Clip to active selection", AutoSize = true };
    private readonly CheckBox referenceLayers = new() { Text = "Include reference layers", AutoSize = true };

    public EffectsPanel(SpriteEditorSession editorSession, Func<IReadOnlyCollection<int>> selectedFrames, Func<Rgba32> foregroundColor, Func<Rgba32> backgroundColor)
    {
        session = editorSession; selectedFrameIndexes = selectedFrames; foreground = foregroundColor; background = backgroundColor; Dock = DockStyle.Fill; Padding = new Padding(8);
        effectBox.Items.AddRange(["Hue / Saturation", "Brightness / Contrast", "Color Curves", "Invert", "Desaturate", "Gaussian Blur", "Sharpen", "Pixelate", "Outline", "Silhouette", "Alpha Cleanup", "Remove Isolated Pixels", "Expand Opaque", "Contract Opaque", "Ordered Dither"]); effectBox.SelectedIndex = 0;
        scopeBox.Items.AddRange(Enum.GetNames<BatchEffectScope>()); scopeBox.SelectedItem = nameof(BatchEffectScope.ActiveCel);
        Button apply = new() { Text = "Apply Effect", Dock = DockStyle.Top, Height = 34 }; apply.Click += (_, _) => Apply();
        TableLayoutPanel root = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 }; root.Controls.Add(Labeled("Effect", effectBox)); root.Controls.Add(Labeled("Scope", scopeBox)); root.Controls.Add(Labeled("Amount / radius", amount1)); root.Controls.Add(Labeled("Second amount", amount2)); root.Controls.Add(selectionOnly); root.Controls.Add(referenceLayers); root.Controls.Add(apply); Controls.Add(root);
    }

    private void Apply()
    {
        try
        {
            BatchEffectScope scope = Enum.TryParse(scopeBox.SelectedItem?.ToString(), out BatchEffectScope parsed) ? parsed : BatchEffectScope.ActiveCel;
            Guid[] frames = selectedFrameIndexes().Where(index => index >= 0 && index < session.Document.Frames.Count).Select(index => session.Document.Frames[index].Id).ToArray();
            BatchEffectSelection batch = new(scope, session.ActiveLayerId, session.ActiveFrameId, [session.SelectedLayerId], frames.Length == 0 ? [session.ActiveFrameId] : frames, referenceLayers.Checked);
            SelectionMask? mask = selectionOnly.Checked ? session.Selection : null; double a = Decimal.ToDouble(amount1.Value), b = Decimal.ToDouble(amount2.Value); string effect = effectBox.SelectedItem?.ToString() ?? "Invert";
            Action<PixelBuffer> operation = effect switch
            {
                "Hue / Saturation" => buffer => AdvancedPixelEffects.HueSaturationLightness(buffer, a, b / 100d, 0d, mask),
                "Brightness / Contrast" => buffer => AdvancedPixelEffects.BrightnessContrast(buffer, a / 100d, b / 100d, mask),
                "Color Curves" => buffer => AdvancedPixelEffects.Curves(buffer, [new(0, 0), new(128, (byte)Math.Clamp((int)Math.Round(128 + a), 0, 255)), new(255, 255)], mask),
                "Desaturate" => buffer => AdvancedPixelEffects.ConvertToGrayscale(buffer, GrayscaleMethod.Luminance, mask),
                "Gaussian Blur" => buffer => AdvancedPixelEffects.GaussianBlur(buffer, Math.Max(1, (int)Math.Round(Math.Abs(a))), mask),
                "Sharpen" => buffer => AdvancedPixelEffects.Sharpen(buffer, Math.Max(.1d, Math.Abs(a) / 10d), mask),
                "Pixelate" => buffer => AdvancedPixelEffects.Pixelate(buffer, Math.Max(2, (int)Math.Round(Math.Abs(a))), mask),
                "Outline" => buffer => AdvancedPixelEffects.Outline(buffer, foreground(), Math.Max(1, (int)Math.Round(Math.Abs(a))), true, false, mask),
                "Silhouette" => buffer => AdvancedPixelEffects.Silhouette(buffer, foreground(), mask),
                "Alpha Cleanup" => buffer => AdvancedPixelEffects.AlphaThreshold(buffer, (byte)Math.Clamp((int)Math.Round(Math.Abs(a)), 0, 255), true, mask),
                "Remove Isolated Pixels" => buffer => AdvancedPixelEffects.RemoveIsolatedPixels(buffer, Math.Clamp((int)Math.Round(Math.Abs(a)), 0, 8), mask),
                "Expand Opaque" => buffer => AdvancedPixelEffects.ExpandOpaque(buffer, Math.Max(1, (int)Math.Round(Math.Abs(a))), foreground(), mask),
                "Contract Opaque" => buffer => AdvancedPixelEffects.ContractOpaque(buffer, Math.Max(1, (int)Math.Round(Math.Abs(a))), mask),
                "Ordered Dither" => buffer => AdvancedPixelEffects.OrderedDither(buffer, foreground(), background(), DitherMatrixKind.Bayer4x4, mask),
                _ => buffer => AdvancedPixelEffects.Invert(buffer, false, mask)
            };
            int changed = session.ApplyBatchEffect(effect, batch, operation); if (changed == 0) MessageBox.Show(this, "No cels matched the selected scope.", "Effect", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Effect failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static Control Labeled(string text, Control control) { TableLayoutPanel panel = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 }; panel.ColumnStyles.Add(new(SizeType.Percent, 45)); panel.ColumnStyles.Add(new(SizeType.Percent, 55)); panel.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0); panel.Controls.Add(control, 1, 0); return panel; }
}
