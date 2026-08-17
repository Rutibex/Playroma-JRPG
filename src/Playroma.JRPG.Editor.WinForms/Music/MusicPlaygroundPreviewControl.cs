using System.Drawing.Drawing2D;
using PlayromaMusic.Core.Model;

namespace Playroma.JRPG.Editor.WinForms.Music;

internal sealed class MusicPlaygroundPreviewControl : Control
{
    private ProjectDocument? _project;
    private MusicScenePreset? _scene;
    private double _progress;
    private bool _isPlaying;

    public MusicPlaygroundPreviewControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        MinimumSize = new Size(300, 180);
        BackColor = Color.FromArgb(28, 33, 46);
        ForeColor = Color.White;
        SetStyle(ControlStyles.Selectable, true);
        AccessibleName = "Music preview";
        AccessibleDescription = "A visual overview of the generated song, its sections, and playback progress.";
    }

    public void SetProject(ProjectDocument? project, MusicScenePreset? scene)
    {
        _project = project;
        _scene = scene;
        Invalidate();
    }

    public void SetPlayback(double progress, bool isPlaying)
    {
        _progress = Math.Clamp(progress, 0d, 1d);
        _isPlaying = isPlaying;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2) return;

        (Color top, Color bottom, Color accent) = Palette(_scene?.Category ?? MusicSceneCategory.Story);
        using (LinearGradientBrush background = new(bounds, top, bottom, LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(background, bounds);
        }

        DrawAtmosphere(graphics, bounds, accent);
        DrawHeading(graphics, bounds);
        DrawWaveform(graphics, bounds, accent);
        DrawSections(graphics, bounds, accent);
        DrawPlayhead(graphics, bounds);
    }

    private void DrawAtmosphere(Graphics graphics, Rectangle bounds, Color accent)
    {
        using SolidBrush soft = new(Color.FromArgb(30, accent));
        int diameter = Math.Max(90, bounds.Width / 5);
        graphics.FillEllipse(soft, bounds.Right - diameter - 20, 15, diameter, diameter);
        graphics.FillEllipse(soft, -diameter / 3, bounds.Height / 3, diameter, diameter);

        using Pen horizon = new(Color.FromArgb(55, Color.White), 1f);
        int y = bounds.Height - 62;
        graphics.DrawLine(horizon, 0, y, bounds.Width, y);
        for (int x = 0; x < bounds.Width; x += 36)
        {
            int peak = 8 + ((x / 36) % 4) * 5;
            graphics.DrawLine(horizon, x, y, x + 18, y - peak);
            graphics.DrawLine(horizon, x + 18, y - peak, x + 36, y);
        }
    }

    private void DrawHeading(Graphics graphics, Rectangle bounds)
    {
        string sceneName = _scene?.Name ?? "Your Game Music";
        string title = _project?.Title ?? "Choose a scene and make music";
        using Font sceneFont = new(Font.FontFamily, Math.Max(9f, Font.Size), FontStyle.Bold);
        using Font titleFont = new(Font.FontFamily, Math.Max(15f, Font.Size + 6f), FontStyle.Bold);
        using SolidBrush primary = new(Color.White);
        using SolidBrush secondary = new(Color.FromArgb(205, 225, 235, 250));
        graphics.DrawString(sceneName.ToUpperInvariant(), sceneFont, secondary, new PointF(18, 16));
        graphics.DrawString(title, titleFont, primary, new RectangleF(16, 40, bounds.Width - 32, 44));

        if (_project is not null)
        {
            double tempo = _project.Song.TempoMap.FirstOrDefault()?.BeatsPerMinute ?? 120d;
            string detail = $"{tempo:0} BPM  •  {_project.Song.Tracks.Count} instrument groups  •  {_project.Song.Sections.Count} sections";
            graphics.DrawString(detail, Font, secondary, new PointF(18, 84));
        }
    }

    private void DrawWaveform(Graphics graphics, Rectangle bounds, Color accent)
    {
        Rectangle wave = new(18, 112, Math.Max(1, bounds.Width - 36), Math.Max(24, bounds.Height - 190));
        if (wave.Height <= 8) return;

        const int bins = 72;
        double[] values = new double[bins];
        if (_project is not null && _project.Song.LengthTicks > 0)
        {
            foreach (AbsoluteNote note in _project.Song.EnumerateNotes())
            {
                int index = Math.Clamp((int)(note.AbsoluteStartTick * bins / Math.Max(1d, _project.Song.LengthTicks)), 0, bins - 1);
                values[index] += Math.Max(0.1, note.Note.Velocity / 127d);
            }
        }
        else
        {
            for (int index = 0; index < bins; index++)
            {
                values[index] = 0.25 + (Math.Sin(index * 0.57) + 1d) * 0.25;
            }
        }

        double maximum = Math.Max(1d, values.DefaultIfEmpty(1d).Max());
        float binWidth = wave.Width / (float)bins;
        using SolidBrush bar = new(Color.FromArgb(210, accent));
        using SolidBrush dim = new(Color.FromArgb(90, Color.White));
        for (int index = 0; index < bins; index++)
        {
            float normalized = (float)(values[index] / maximum);
            float height = Math.Max(3f, normalized * wave.Height);
            float x = wave.Left + index * binWidth;
            float y = wave.Bottom - height;
            graphics.FillRectangle(index / (double)bins <= _progress ? bar : dim, x, y, Math.Max(1f, binWidth - 2f), height);
        }
    }

    private void DrawSections(Graphics graphics, Rectangle bounds, Color accent)
    {
        Rectangle timeline = new(18, bounds.Bottom - 52, Math.Max(1, bounds.Width - 36), 28);
        ProjectDocument? project = _project;
        if (project is null || project.Song.Sections.Count == 0 || project.Song.LengthTicks <= 0)
        {
            using SolidBrush empty = new(Color.FromArgb(60, Color.White));
            graphics.FillRoundedRectangle(empty, timeline, 6);
            return;
        }

        foreach (SongSection section in project.Song.Sections.OrderBy(item => item.StartTick))
        {
            float left = timeline.Left + (float)(section.StartTick / (double)project.Song.LengthTicks * timeline.Width);
            float width = Math.Max(5f, (float)(section.LengthTicks / (double)project.Song.LengthTicks * timeline.Width) - 2f);
            RectangleF segment = new(left, timeline.Top, width, timeline.Height);
            int alpha = 105 + (int)(Math.Clamp(section.Energy, 0d, 1d) * 115d);
            using SolidBrush fill = new(Color.FromArgb(alpha, accent));
            graphics.FillRoundedRectangle(fill, segment, 5f);
            if (width > 54)
            {
                using SolidBrush text = new(Color.White);
                using StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                graphics.DrawString(section.Name, Font, text, segment, format);
            }
        }
    }

    private void DrawPlayhead(Graphics graphics, Rectangle bounds)
    {
        if (!_isPlaying && _progress <= 0d) return;
        float x = 18 + (float)(_progress * Math.Max(1, bounds.Width - 36));
        using Pen pen = new(Color.White, 2f);
        graphics.DrawLine(pen, x, 106, x, bounds.Bottom - 18);
        using SolidBrush marker = new(Color.White);
        PointF[] points = [new(x - 5, 106), new(x + 5, 106), new(x, 114)];
        graphics.FillPolygon(marker, points);
    }

    private static (Color Top, Color Bottom, Color Accent) Palette(MusicSceneCategory category) => category switch
    {
        MusicSceneCategory.Battle => (Color.FromArgb(54, 25, 34), Color.FromArgb(21, 24, 34), Color.FromArgb(242, 103, 90)),
        MusicSceneCategory.Place => (Color.FromArgb(22, 61, 58), Color.FromArgb(18, 28, 40), Color.FromArgb(80, 205, 164)),
        MusicSceneCategory.Travel => (Color.FromArgb(25, 55, 83), Color.FromArgb(17, 26, 41), Color.FromArgb(88, 170, 242)),
        MusicSceneCategory.Celebration => (Color.FromArgb(78, 54, 20), Color.FromArgb(33, 27, 35), Color.FromArgb(245, 190, 70)),
        MusicSceneCategory.Danger => (Color.FromArgb(57, 27, 66), Color.FromArgb(18, 22, 31), Color.FromArgb(190, 104, 220)),
        _ => (Color.FromArgb(42, 42, 75), Color.FromArgb(18, 24, 38), Color.FromArgb(138, 150, 245))
    };
}

internal static class GraphicsRoundedRectangleExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius) =>
        graphics.FillRoundedRectangle(brush, new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), radius);

    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF rectangle, float radius)
    {
        float diameter = Math.Max(1f, radius * 2f);
        using GraphicsPath path = new();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
