using System.Drawing.Drawing2D;
using PlayromaMusic.Core.Model;

namespace Playroma.JRPG.Editor.WinForms.Music;

internal sealed class MusicSectionTimelineControl : Control
{
    private ProjectDocument? _project;
    private string? _selectedSectionId;
    private readonly List<(SongSection Section, Rectangle Bounds)> _hitTargets = [];

    public MusicSectionTimelineControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(246, 247, 250);
        MinimumSize = new Size(400, 120);
        Height = 150;
        AccessibleName = "Song structure timeline";
        AccessibleDescription = "Select a song section to make it stronger, calmer, or duplicate it.";
        TabStop = true;
    }

    public event EventHandler? SelectedSectionChanged;

    public ProjectDocument? Project
    {
        get => _project;
        set
        {
            _project = value;
            if (_project?.Song.Sections.Any(section => string.Equals(section.Id, _selectedSectionId, StringComparison.Ordinal)) != true)
            {
                _selectedSectionId = _project?.Song.Sections.FirstOrDefault()?.Id;
            }
            Invalidate();
        }
    }

    public string? SelectedSectionId
    {
        get => _selectedSectionId;
        set
        {
            if (string.Equals(_selectedSectionId, value, StringComparison.Ordinal)) return;
            _selectedSectionId = value;
            Invalidate();
            SelectedSectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SongSection? SelectedSection => _project?.Song.Sections.FirstOrDefault(section =>
        string.Equals(section.Id, _selectedSectionId, StringComparison.Ordinal));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);
        _hitTargets.Clear();

        using SolidBrush headingBrush = new(Color.FromArgb(44, 48, 58));
        using Font headingFont = new(Font.FontFamily, Font.Size + 1f, FontStyle.Bold);
        graphics.DrawString("SONG STRUCTURE", headingFont, headingBrush, new PointF(12, 10));

        ProjectDocument? project = _project;
        if (project is null || project.Song.Sections.Count == 0 || project.Song.LengthTicks <= 0)
        {
            using SolidBrush empty = new(Color.FromArgb(115, 120, 132));
            graphics.DrawString("Generate a song to see its intro, themes, builds, and ending.", Font, empty, new PointF(12, 52));
            return;
        }

        Rectangle track = new(12, 42, Math.Max(20, ClientSize.Width - 24), Math.Max(48, ClientSize.Height - 66));
        foreach (SongSection section in project.Song.Sections.OrderBy(item => item.StartTick))
        {
            float left = track.Left + (float)(section.StartTick / (double)project.Song.LengthTicks * track.Width);
            float width = Math.Max(26f, (float)(section.LengthTicks / (double)project.Song.LengthTicks * track.Width) - 4f);
            Rectangle bounds = Rectangle.Round(new RectangleF(left, track.Top, Math.Min(width, track.Right - left), track.Height));
            bool selected = string.Equals(section.Id, _selectedSectionId, StringComparison.Ordinal);
            Color baseColor = EnergyColor(section.Energy);
            using SolidBrush fill = new(selected ? baseColor : Color.FromArgb(190, baseColor));
            graphics.FillRoundedRectangle(fill, bounds, 8);
            using Pen border = new(selected ? Color.FromArgb(30, 40, 68) : Color.FromArgb(80, 90, 110), selected ? 3f : 1f);
            using GraphicsPath path = RoundedRectangle(bounds, 8);
            graphics.DrawPath(border, path);

            using SolidBrush text = new(selected ? Color.White : Color.FromArgb(28, 32, 42));
            using Font sectionFont = new(Font.FontFamily, Font.Size, selected ? FontStyle.Bold : FontStyle.Regular);
            using StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            string caption = bounds.Width >= 100 ? $"{section.Name}\nEnergy {section.Energy:P0}" : section.Name;
            graphics.DrawString(caption, sectionFont, text, bounds, format);
            _hitTargets.Add((section, bounds));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        (SongSection Section, Rectangle Bounds)? hit = _hitTargets.FirstOrDefault(item => item.Bounds.Contains(e.Location));
        if (hit is { } value && value.Section is not null)
        {
            SelectedSectionId = value.Section.Id;
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_project is null || _project.Song.Sections.Count == 0 || e.KeyCode is not (Keys.Left or Keys.Right)) return;
        List<SongSection> ordered = _project.Song.Sections.OrderBy(item => item.StartTick).ToList();
        int index = Math.Max(0, ordered.FindIndex(item => string.Equals(item.Id, _selectedSectionId, StringComparison.Ordinal)));
        index = e.KeyCode == Keys.Left ? Math.Max(0, index - 1) : Math.Min(ordered.Count - 1, index + 1);
        SelectedSectionId = ordered[index].Id;
        e.Handled = true;
    }

    private static Color EnergyColor(double energy)
    {
        energy = Math.Clamp(energy, 0d, 1d);
        int red = (int)(85 + energy * 145);
        int green = (int)(170 - energy * 60);
        int blue = (int)(220 - energy * 100);
        return Color.FromArgb(red, green, blue);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        GraphicsPath path = new();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
