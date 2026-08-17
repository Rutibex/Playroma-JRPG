using Playroma.JRPG.Assets;

namespace Playroma.JRPG.Editor.WinForms.Assets;

public sealed class WaveformControl : Control
{
    private WaveformEnvelope? _waveform;
    private double _positionFraction;
    private double? _loopStartFraction;
    private double? _loopEndFraction;

    public WaveformControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        MinimumSize = new Size(100, 80);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.FromArgb(120, 190, 255);
    }

    public WaveformEnvelope? Waveform
    {
        get => _waveform;
        set { _waveform = value; Invalidate(); }
    }

    public double PositionFraction
    {
        get => _positionFraction;
        set { _positionFraction = Math.Clamp(value, 0d, 1d); Invalidate(); }
    }

    public double? LoopStartFraction
    {
        get => _loopStartFraction;
        set { _loopStartFraction = value; Invalidate(); }
    }

    public double? LoopEndFraction
    {
        get => _loopEndFraction;
        set { _loopEndFraction = value; Invalidate(); }
    }


    public void SetWaveform(WaveformEnvelope? waveform, long? loopStartSamples = null, long? loopLengthSamples = null)
    {
        Waveform = waveform;
        PositionFraction = 0d;
        if (waveform is null || waveform.SampleRate <= 0 || waveform.Duration <= TimeSpan.Zero || !loopStartSamples.HasValue)
        {
            LoopStartFraction = null;
            LoopEndFraction = null;
            return;
        }

        double totalFrames = waveform.Duration.TotalSeconds * waveform.SampleRate;
        if (totalFrames <= 0)
        {
            LoopStartFraction = null;
            LoopEndFraction = null;
            return;
        }
        LoopStartFraction = Math.Clamp(loopStartSamples.Value / totalFrames, 0d, 1d);
        LoopEndFraction = loopLengthSamples.HasValue
            ? Math.Clamp((loopStartSamples.Value + loopLengthSamples.Value) / totalFrames, 0d, 1d)
            : null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;
        e.Graphics.Clear(BackColor);
        using Pen centrePen = new(Color.FromArgb(90, ForeColor));
        int centre = bounds.Top + bounds.Height / 2;
        e.Graphics.DrawLine(centrePen, bounds.Left, centre, bounds.Right, centre);

        if (_waveform is not null && _waveform.Buckets.Count > 0)
        {
            using Pen waveformPen = new(ForeColor);
            IReadOnlyList<WaveformBucket> buckets = _waveform.Buckets;
            for (int x = 0; x < bounds.Width; x++)
            {
                int index = Math.Min(buckets.Count - 1, x * buckets.Count / Math.Max(1, bounds.Width));
                WaveformBucket bucket = buckets[index];
                int top = centre - (int)Math.Round(Math.Clamp(bucket.Maximum, -1f, 1f) * bounds.Height * 0.45);
                int bottom = centre - (int)Math.Round(Math.Clamp(bucket.Minimum, -1f, 1f) * bounds.Height * 0.45);
                e.Graphics.DrawLine(waveformPen, bounds.Left + x, top, bounds.Left + x, bottom);
            }
        }
        else
        {
            TextRenderer.DrawText(e.Graphics, "Waveform unavailable", Font, bounds, Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (_loopStartFraction.HasValue && _loopEndFraction.HasValue)
        {
            int left = bounds.Left + (int)Math.Round(bounds.Width * Math.Clamp(_loopStartFraction.Value, 0d, 1d));
            int right = bounds.Left + (int)Math.Round(bounds.Width * Math.Clamp(_loopEndFraction.Value, 0d, 1d));
            using SolidBrush loopBrush = new(Color.FromArgb(35, Color.Gold));
            e.Graphics.FillRectangle(loopBrush, Math.Min(left, right), bounds.Top, Math.Abs(right - left), bounds.Height);
            using Pen loopPen = new(Color.Gold);
            e.Graphics.DrawLine(loopPen, left, bounds.Top, left, bounds.Bottom);
            e.Graphics.DrawLine(loopPen, right, bounds.Top, right, bounds.Bottom);
        }

        int playhead = bounds.Left + (int)Math.Round(bounds.Width * _positionFraction);
        using Pen playheadPen = new(Color.OrangeRed, 2f);
        e.Graphics.DrawLine(playheadPen, playhead, bounds.Top, playhead, bounds.Bottom);
    }
}
