using Playroma.Sound.Analysis.Profiling;
using Playroma.Sound.Analysis.Realtime;
using Playroma.Sound.WinForms.Playback;

namespace Playroma.Sound.WinForms.Controls;

/// <summary>Live waveform, spectrum, spectrogram, metering, and runtime-profiler presentation.</summary>
public sealed class AnalysisProfilerControl : UserControl
{
    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly Label _metrics = new() { AutoSize = true, Padding = new Padding(8, 6, 4, 0) };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly WaveformView _waveform = new() { Dock = DockStyle.Fill };
    private readonly SpectrumView _spectrum = new() { Dock = DockStyle.Fill };
    private readonly SpectrogramView _spectrogram = new() { Dock = DockStyle.Fill };
    private readonly ProfilerView _profiler = new() { Dock = DockStyle.Fill };

    public AnalysisProfilerControl()
    {
        Dock = DockStyle.Fill;
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(4) };
        header.Controls.Add(new Label { Text = "Source", AutoSize = true, Padding = new Padding(0, 7, 4, 0) });
        header.Controls.Add(_source);
        header.Controls.Add(_metrics);
        _source.Items.Add("Master output");
        _source.SelectedIndex = 0;
        _tabs.TabPages.Add(CreateTab("Waveform", _waveform));
        _tabs.TabPages.Add(CreateTab("Spectrum", _spectrum));
        _tabs.TabPages.Add(CreateTab("Spectrogram", _spectrogram));
        _tabs.TabPages.Add(CreateTab("Profiler", _profiler));
        Controls.Add(_tabs);
        Controls.Add(header);
    }

    public void UpdateFrom(DocumentPreviewSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string previous = _source.SelectedItem?.ToString() ?? "Master output";
        string[] buses = session.BusAnalysis?.EnabledBusIds.ToArray() ?? [];
        string[] desired = ["Master output", .. buses.Select(id => "Bus: " + id)];
        if (_source.Items.Count != desired.Length || !_source.Items.Cast<string>().SequenceEqual(desired, StringComparer.Ordinal))
        {
            _source.BeginUpdate();
            _source.Items.Clear();
            _source.Items.AddRange(desired);
            _source.SelectedItem = desired.Contains(previous, StringComparer.Ordinal) ? previous : desired[0];
            _source.EndUpdate();
        }
        AudioAnalysisSnapshot snapshot = SelectedSnapshot(session);
        _metrics.Text = $"Peak {ToDb(snapshot.Peak),6:0.0} dB  RMS {ToDb(snapshot.Rms),6:0.0} dB  Loudness {snapshot.EstimatedLoudnessLufs,6:0.0} LUFS  DC {snapshot.DcOffset,7:0.0000}  Phase {snapshot.StereoCorrelation,5:0.00}  Clips {snapshot.ClippedSamples}";
        _waveform.Snapshot = snapshot;
        _spectrum.Snapshot = snapshot;
        _spectrogram.Snapshot = snapshot;
        _profiler.Samples = session.Profiler.CaptureSamples();
        _profiler.Summary = session.Profiler.CaptureSummary();
        _waveform.Invalidate();
        _spectrum.Invalidate();
        _spectrogram.Invalidate();
        _profiler.Invalidate();
    }

    private AudioAnalysisSnapshot SelectedSnapshot(DocumentPreviewSession session)
    {
        string selected = _source.SelectedItem?.ToString() ?? "Master output";
        if (selected.StartsWith("Bus: ", StringComparison.Ordinal))
            return session.BusAnalysis?.Capture(selected[5..]) ?? session.MasterAnalysis;
        return session.MasterAnalysis;
    }

    private static TabPage CreateTab(string text, Control control)
    {
        var page = new TabPage(text);
        page.Controls.Add(control);
        return page;
    }

    private static float ToDb(float linear) => linear <= 0.0000001f ? -140f : 20f * MathF.Log10(linear);

    private abstract class PlotView : Control
    {
        protected PlotView() { DoubleBuffered = true; BackColor = Color.FromArgb(20, 22, 26); ForeColor = Color.Gainsboro; }
        protected static void DrawGrid(Graphics graphics, Rectangle bounds, int vertical, int horizontal)
        {
            using var pen = new Pen(Color.FromArgb(45, 50, 58));
            for (int i = 1; i < vertical; i++) { int x = bounds.Left + (bounds.Width * i / vertical); graphics.DrawLine(pen, x, bounds.Top, x, bounds.Bottom); }
            for (int i = 1; i < horizontal; i++) { int y = bounds.Top + (bounds.Height * i / horizontal); graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y); }
        }
    }

    private sealed class WaveformView : PlotView
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibilityAttribute(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public AudioAnalysisSnapshot? Snapshot { get; set; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = ClientRectangle;
            DrawGrid(e.Graphics, bounds, 8, 4);
            AudioAnalysisSnapshot? snapshot = Snapshot;
            if (snapshot is null || snapshot.WaveformMinimum.Count < 2) return;
            using var pen = new Pen(Color.DeepSkyBlue, 1.2f);
            int middle = bounds.Top + bounds.Height / 2;
            for (int i = 0; i < snapshot.WaveformMinimum.Count; i++)
            {
                int x = bounds.Left + (i * Math.Max(1, bounds.Width - 1) / Math.Max(1, snapshot.WaveformMinimum.Count - 1));
                int y1 = middle - (int)(snapshot.WaveformMaximum[i] * bounds.Height * 0.45f);
                int y2 = middle - (int)(snapshot.WaveformMinimum[i] * bounds.Height * 0.45f);
                e.Graphics.DrawLine(pen, x, y1, x, y2);
            }
        }
    }

    private sealed class SpectrumView : PlotView
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibilityAttribute(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public AudioAnalysisSnapshot? Snapshot { get; set; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = ClientRectangle;
            DrawGrid(e.Graphics, bounds, 10, 7);
            AudioAnalysisSnapshot? snapshot = Snapshot;
            if (snapshot is null || snapshot.SpectrumDecibels.Count < 2) return;
            using var pen = new Pen(Color.LimeGreen, 1.4f);
            PointF[] points = new PointF[snapshot.SpectrumDecibels.Count];
            for (int i = 0; i < points.Length; i++)
            {
                float normalized = Math.Clamp((snapshot.SpectrumDecibels[i] + 120f) / 120f, 0f, 1f);
                points[i] = new PointF(bounds.Left + i * Math.Max(1, bounds.Width - 1f) / (points.Length - 1), bounds.Bottom - normalized * bounds.Height);
            }
            e.Graphics.DrawLines(pen, points);
        }
    }

    private sealed class SpectrogramView : PlotView
    {
        private readonly SolidBrush[] _palette = Enumerable.Range(0, 128).Select(index =>
        {
            float level = index / 127f;
            int red = (int)(255 * Math.Clamp((level - 0.45f) * 1.8f, 0f, 1f));
            int green = (int)(255 * Math.Clamp(level * 1.35f, 0f, 1f));
            int blue = (int)(255 * Math.Clamp((0.8f - level) * 1.2f, 0f, 1f));
            return new SolidBrush(Color.FromArgb(red, green, blue));
        }).ToArray();

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibilityAttribute(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public AudioAnalysisSnapshot? Snapshot { get; set; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            AudioAnalysisSnapshot? snapshot = Snapshot;
            if (snapshot is null || snapshot.SpectrogramDecibels.Count == 0) return;
            int rows = snapshot.SpectrogramDecibels.Count;
            for (int row = 0; row < rows; row++)
            {
                IReadOnlyList<float> bins = snapshot.SpectrogramDecibels[row];
                if (bins.Count == 0) continue;
                float y1 = row * Height / (float)rows;
                float y2 = (row + 1) * Height / (float)rows;
                for (int bin = 0; bin < bins.Count; bin++)
                {
                    float level = Math.Clamp((bins[bin] + 120f) / 120f, 0f, 1f);
                    SolidBrush brush = _palette[Math.Clamp((int)MathF.Round(level * (_palette.Length - 1)), 0, _palette.Length - 1)];
                    float x1 = bin * Width / (float)bins.Count;
                    float x2 = (bin + 1) * Width / (float)bins.Count;
                    e.Graphics.FillRectangle(brush, x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (SolidBrush brush in _palette) brush.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ProfilerView : PlotView
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibilityAttribute(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<RealtimeProfileSample> Samples { get; set; } = [];

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibilityAttribute(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RealtimeProfileSummary Summary { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle graph = new(0, 22, Width, Math.Max(1, Height - 22));
            DrawGrid(e.Graphics, graph, 10, 5);
            string text = $"Producer avg {Summary.AverageProducerMilliseconds:0.00} ms  P95 {Summary.P95ProducerMilliseconds:0.00}  peak {Summary.PeakProducerMilliseconds:0.00}  " +
                          $"Load avg {Summary.AverageRenderLoadRatio:P0}  peak {Summary.PeakRenderLoadRatio:P0}  voices {Summary.PeakActiveVoices}  underrun {Summary.LatestUnderrunFrames}";
            TextRenderer.DrawText(e.Graphics, text, Font, new Point(4, 3), ForeColor);
            if (Samples.Count < 2) return;
            double maximum = Math.Max(1, Samples.Max(sample => Math.Max(sample.AudioDeadlineMilliseconds, sample.ProducerMilliseconds)));
            using var producerPen = new Pen(Color.Orange, 1.3f);
            using var deadlinePen = new Pen(Color.Red, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            PointF[] producer = new PointF[Samples.Count];
            PointF[] deadline = new PointF[Samples.Count];
            for (int i = 0; i < Samples.Count; i++)
            {
                float x = graph.Left + i * Math.Max(1, graph.Width - 1f) / (Samples.Count - 1);
                producer[i] = new PointF(x, graph.Bottom - (float)(Samples[i].ProducerMilliseconds / maximum * graph.Height));
                deadline[i] = new PointF(x, graph.Bottom - (float)(Samples[i].AudioDeadlineMilliseconds / maximum * graph.Height));
            }
            e.Graphics.DrawLines(producerPen, producer);
            e.Graphics.DrawLines(deadlinePen, deadline);
        }
    }
}
