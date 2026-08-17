using PlayromaMusic.Core.Model;

namespace Playroma.JRPG.Editor.WinForms.Music;

public enum PianoRollEditOperation
{
    Add,
    Move,
    Delete,
    ChangeVelocity
}

public sealed record PianoRollEditRequest(
    PianoRollEditOperation Operation,
    string TrackId,
    string ClipId,
    string NoteId,
    long AbsoluteTick,
    int Pitch,
    int VelocityDelta = 0);

public sealed class PianoRollEditEventArgs(PianoRollEditRequest request) : EventArgs
{
    public PianoRollEditRequest Request { get; } = request;
}

/// <summary>
/// Interactive WinForms piano roll. Left-click empty space to add, drag a note to move it,
/// right-click a note to delete it, and use the mouse wheel over the selected note to alter velocity.
/// Changes are requested through <see cref="EditRequested"/> so the host can capture undo history.
/// </summary>
public sealed class PianoRollControl : Control
{
    private ProjectDocument? _project;
    private string? _trackId;
    private string? _selectedNoteId;
    private DragState? _drag;
    private Point _lastMouse;

    public PianoRollControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(28, 31, 38);
        ForeColor = Color.Gainsboro;
        MinimumSize = new Size(320, 180);
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    public event EventHandler<PianoRollEditEventArgs>? EditRequested;

    public ProjectDocument? Project
    {
        get => _project;
        set
        {
            _project = value;
            if (!ContainsNote(_selectedNoteId)) _selectedNoteId = null;
            Invalidate();
        }
    }

    public string? TrackId
    {
        get => _trackId;
        set
        {
            _trackId = value;
            if (!ContainsNote(_selectedNoteId)) _selectedNoteId = null;
            Invalidate();
        }
    }

    public string? SelectedNoteId
    {
        get => _selectedNoteId;
        set { _selectedNoteId = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_project is null || _project.Song.LengthTicks <= 0)
        {
            TextRenderer.DrawText(e.Graphics, "No notes to display", Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        Rectangle plot = GetPlotRectangle();
        DrawGrid(e.Graphics, plot, _project);
        DrawSections(e.Graphics, plot, _project);
        DrawNotes(e.Graphics, plot, _project);
        DrawDragPreview(e.Graphics, plot, _project);
        e.Graphics.DrawRectangle(Pens.DimGray, plot);
        TextRenderer.DrawText(e.Graphics,
            "Left-click: add/select · Drag: move · Right-click: delete · Wheel: velocity",
            SystemFonts.MessageBoxFont,
            new Rectangle(plot.Left, Math.Max(0, ClientSize.Height - 22), plot.Width, 18),
            Color.Silver,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _lastMouse = e.Location;
        if (_project is null || ActiveTrack() is not { } track) return;

        if (TryHitNote(e.Location, track, out SongClip? clip, out NoteEvent? note, out _))
        {
            _selectedNoteId = note.Id;
            Invalidate();
            if (e.Button == MouseButtons.Right)
            {
                EditRequested?.Invoke(this, new PianoRollEditEventArgs(new PianoRollEditRequest(
                    PianoRollEditOperation.Delete,
                    track.Id,
                    clip.Id,
                    note.Id,
                    clip.StartTick + note.StartTick,
                    note.Pitch)));
                _selectedNoteId = null;
                return;
            }

            if (e.Button == MouseButtons.Left && TryMapPoint(e.Location, out long mappedTick, out int mappedPitch))
            {
                _drag = new DragState(
                    track.Id,
                    clip.Id,
                    note.Id,
                    clip.StartTick + note.StartTick,
                    note.Pitch,
                    clip.StartTick + note.StartTick - mappedTick,
                    note.Pitch - mappedPitch,
                    clip.StartTick + note.StartTick,
                    note.Pitch);
                Capture = true;
            }
            return;
        }

        if (e.Button != MouseButtons.Left || !TryMapPoint(e.Location, out long absoluteTick, out int pitch)) return;
        SongClip? targetClip = FindClipAtTick(track, absoluteTick);
        if (targetClip is null) return;

        string noteId = "manual-" + Guid.NewGuid().ToString("N");
        _selectedNoteId = noteId;
        EditRequested?.Invoke(this, new PianoRollEditEventArgs(new PianoRollEditRequest(
            PianoRollEditOperation.Add,
            track.Id,
            targetClip.Id,
            noteId,
            absoluteTick,
            pitch)));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _lastMouse = e.Location;
        if (_drag is null || _project is null || ActiveTrack() is not { } track ||
            !TryMapPoint(e.Location, out long mappedTick, out int mappedPitch))
        {
            return;
        }

        SongClip? clip = track.Clips.FirstOrDefault(item => string.Equals(item.Id, _drag.ClipId, StringComparison.Ordinal));
        NoteEvent? note = clip?.Notes.FirstOrDefault(item => string.Equals(item.Id, _drag.NoteId, StringComparison.Ordinal));
        if (clip is null || note is null) return;

        long absoluteTick = Math.Clamp(
            mappedTick + _drag.TickOffset,
            clip.StartTick,
            Math.Max(clip.StartTick, clip.EndTick - Math.Max(1, note.DurationTicks)));
        int pitch = Math.Clamp(mappedPitch + _drag.PitchOffset, 0, 127);
        _drag = _drag with { PreviewAbsoluteTick = absoluteTick, PreviewPitch = pitch };
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag is not { } drag) return;
        _drag = null;
        Capture = false;
        Invalidate();
        if (drag.PreviewAbsoluteTick == drag.OriginalAbsoluteTick && drag.PreviewPitch == drag.OriginalPitch) return;

        EditRequested?.Invoke(this, new PianoRollEditEventArgs(new PianoRollEditRequest(
            PianoRollEditOperation.Move,
            drag.TrackId,
            drag.ClipId,
            drag.NoteId,
            drag.PreviewAbsoluteTick,
            drag.PreviewPitch)));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_project is null || ActiveTrack() is not { } track || string.IsNullOrWhiteSpace(_selectedNoteId)) return;
        foreach (SongClip clip in track.Clips)
        {
            NoteEvent? note = clip.Notes.FirstOrDefault(item => string.Equals(item.Id, _selectedNoteId, StringComparison.Ordinal));
            if (note is null) continue;
            EditRequested?.Invoke(this, new PianoRollEditEventArgs(new PianoRollEditRequest(
                PianoRollEditOperation.ChangeVelocity,
                track.Id,
                clip.Id,
                note.Id,
                clip.StartTick + note.StartTick,
                note.Pitch,
                e.Delta > 0 ? 5 : -5)));
            return;
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        if (keyData is Keys.Delete or Keys.Back) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Delete or Keys.Back) || _project is null || ActiveTrack() is not { } track ||
            string.IsNullOrWhiteSpace(_selectedNoteId)) return;
        foreach (SongClip clip in track.Clips)
        {
            NoteEvent? note = clip.Notes.FirstOrDefault(item => string.Equals(item.Id, _selectedNoteId, StringComparison.Ordinal));
            if (note is null) continue;
            EditRequested?.Invoke(this, new PianoRollEditEventArgs(new PianoRollEditRequest(
                PianoRollEditOperation.Delete,
                track.Id,
                clip.Id,
                note.Id,
                clip.StartTick + note.StartTick,
                note.Pitch)));
            _selectedNoteId = null;
            e.Handled = true;
            return;
        }
    }

    private Rectangle GetPlotRectangle() => new(48, 24, Math.Max(1, ClientSize.Width - 56), Math.Max(1, ClientSize.Height - 56));

    private SongTrack? ActiveTrack()
    {
        if (_project is null) return null;
        if (!string.IsNullOrWhiteSpace(_trackId))
        {
            return _project.Song.Tracks.FirstOrDefault(track => string.Equals(track.Id, _trackId, StringComparison.Ordinal));
        }
        return _project.Song.Tracks.FirstOrDefault(track => !track.Muted);
    }

    private bool ContainsNote(string? noteId)
    {
        if (_project is null || string.IsNullOrWhiteSpace(noteId)) return false;
        return _project.Song.Tracks.SelectMany(track => track.Clips).SelectMany(clip => clip.Notes)
            .Any(note => string.Equals(note.Id, noteId, StringComparison.Ordinal));
    }

    private bool TryHitNote(Point point, SongTrack track, out SongClip? hitClip, out NoteEvent? hitNote, out Rectangle bounds)
    {
        if (_project is null)
        {
            hitClip = null;
            hitNote = null;
            bounds = Rectangle.Empty;
            return false;
        }
        Rectangle plot = GetPlotRectangle();
        foreach (SongClip clip in track.Clips.AsEnumerable().Reverse())
        {
            foreach (NoteEvent note in clip.Notes.AsEnumerable().Reverse())
            {
                Rectangle candidate = GetNoteBounds(plot, _project, clip, note);
                if (!candidate.Contains(point)) continue;
                hitClip = clip;
                hitNote = note;
                bounds = candidate;
                return true;
            }
        }
        hitClip = null;
        hitNote = null;
        bounds = Rectangle.Empty;
        return false;
    }

    private bool TryMapPoint(Point point, out long absoluteTick, out int pitch)
    {
        if (_project is null)
        {
            absoluteTick = 0;
            pitch = 60;
            return false;
        }
        Rectangle plot = GetPlotRectangle();
        if (!plot.Contains(point))
        {
            absoluteTick = 0;
            pitch = 60;
            return false;
        }
        double normalizedX = Math.Clamp((point.X - plot.Left) / (double)Math.Max(1, plot.Width), 0, 1);
        long rawTick = checked((long)Math.Round(normalizedX * _project.Song.LengthTicks));
        long snap = Math.Max(1, _project.Song.TicksPerQuarterNote / 2L);
        absoluteTick = Math.Clamp(checked((long)Math.Round(rawTick / (double)snap) * snap), 0, Math.Max(0, _project.Song.LengthTicks - snap));
        const int low = 24;
        const int high = 108;
        double normalizedY = Math.Clamp((point.Y - plot.Top) / (double)Math.Max(1, plot.Height), 0, 1);
        pitch = Math.Clamp(high - checked((int)Math.Floor(normalizedY * (high - low + 1))), low, high);
        return true;
    }

    private static SongClip? FindClipAtTick(SongTrack track, long absoluteTick) =>
        track.Clips.FirstOrDefault(clip => absoluteTick >= clip.StartTick && absoluteTick < clip.EndTick)
        ?? track.Clips.OrderBy(clip => Math.Abs(clip.StartTick - absoluteTick)).FirstOrDefault();

    private static void DrawGrid(Graphics graphics, Rectangle plot, ProjectDocument project)
    {
        using Pen semitone = new(Color.FromArgb(45, 49, 58));
        using Pen octave = new(Color.FromArgb(75, 80, 92));
        for (int pitch = 24; pitch <= 108; pitch++)
        {
            int y = PitchToY(pitch, plot);
            graphics.DrawLine(pitch % 12 == 0 ? octave : semitone, plot.Left, y, plot.Right, y);
            if (pitch % 12 == 0)
            {
                TextRenderer.DrawText(graphics, "C" + ((pitch / 12) - 1), SystemFonts.MessageBoxFont,
                    new Rectangle(2, y - 8, 44, 16), Color.Silver, TextFormatFlags.Right);
            }
        }

        long beatTicks = Math.Max(1, project.Song.TicksPerQuarterNote);
        long barTicks = beatTicks * Math.Max(1, project.Song.MeterMap.FirstOrDefault()?.Numerator ?? 4);
        for (long tick = 0; tick <= project.Song.LengthTicks; tick += beatTicks)
        {
            int x = TickToX(tick, plot, project.Song.LengthTicks);
            bool bar = tick % barTicks == 0;
            using Pen pen = new(bar ? Color.FromArgb(110, 115, 128) : Color.FromArgb(56, 60, 69));
            graphics.DrawLine(pen, x, plot.Top, x, plot.Bottom);
            if (bar)
            {
                int number = checked((int)(tick / barTicks)) + 1;
                TextRenderer.DrawText(graphics, number.ToString(), SystemFonts.MessageBoxFont,
                    new Rectangle(x + 2, 2, 40, 18), Color.Silver);
            }
        }
    }

    private static void DrawSections(Graphics graphics, Rectangle plot, ProjectDocument project)
    {
        for (int index = 0; index < project.Song.Sections.Count; index++)
        {
            SongSection section = project.Song.Sections[index];
            int x1 = TickToX(section.StartTick, plot, project.Song.LengthTicks);
            int x2 = TickToX(section.EndTick, plot, project.Song.LengthTicks);
            Color tint = index % 2 == 0 ? Color.FromArgb(18, 80, 110, 135) : Color.FromArgb(18, 120, 85, 130);
            using Brush brush = new SolidBrush(tint);
            graphics.FillRectangle(brush, x1, plot.Top, Math.Max(1, x2 - x1), plot.Height);
            TextRenderer.DrawText(graphics, section.Name, SystemFonts.MessageBoxFont,
                new Rectangle(x1 + 3, plot.Top + 2, Math.Max(1, x2 - x1 - 6), 18), Color.LightSteelBlue,
                TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawNotes(Graphics graphics, Rectangle plot, ProjectDocument project)
    {
        IEnumerable<SongTrack> tracks = string.IsNullOrWhiteSpace(_trackId)
            ? project.Song.Tracks
            : project.Song.Tracks.Where(track => string.Equals(track.Id, _trackId, StringComparison.Ordinal));
        int trackIndex = 0;
        Color[] palette =
        [
            Color.FromArgb(100, 190, 245), Color.FromArgb(245, 180, 90), Color.FromArgb(145, 220, 135),
            Color.FromArgb(220, 125, 190), Color.FromArgb(190, 155, 245), Color.FromArgb(235, 110, 105),
            Color.FromArgb(110, 220, 210), Color.FromArgb(225, 220, 105)
        ];
        foreach (SongTrack track in tracks.Where(item => !item.Muted))
        {
            using Brush brush = new SolidBrush(palette[trackIndex++ % palette.Length]);
            foreach (SongClip clip in track.Clips)
            {
                foreach (NoteEvent note in clip.Notes)
                {
                    if (_drag?.NoteId == note.Id) continue;
                    Rectangle noteBounds = GetNoteBounds(plot, project, clip, note);
                    graphics.FillRectangle(brush, noteBounds);
                    if (string.Equals(_selectedNoteId, note.Id, StringComparison.Ordinal))
                    {
                        using Pen selection = new(Color.White, 2f);
                        graphics.DrawRectangle(selection, noteBounds);
                    }
                }
            }
        }
    }

    private void DrawDragPreview(Graphics graphics, Rectangle plot, ProjectDocument project)
    {
        if (_drag is not { } drag || ActiveTrack() is not { } track) return;
        SongClip? clip = track.Clips.FirstOrDefault(item => string.Equals(item.Id, drag.ClipId, StringComparison.Ordinal));
        NoteEvent? note = clip?.Notes.FirstOrDefault(item => string.Equals(item.Id, drag.NoteId, StringComparison.Ordinal));
        if (clip is null || note is null) return;
        NoteEvent preview = new()
        {
            Pitch = drag.PreviewPitch,
            StartTick = Math.Max(0, drag.PreviewAbsoluteTick - clip.StartTick),
            DurationTicks = note.DurationTicks,
            Velocity = note.Velocity
        };
        Rectangle bounds = GetNoteBounds(plot, project, clip, preview);
        using Brush brush = new SolidBrush(Color.FromArgb(210, 255, 220, 110));
        using Pen pen = new(Color.White, 2f);
        graphics.FillRectangle(brush, bounds);
        graphics.DrawRectangle(pen, bounds);
        TextRenderer.DrawText(graphics, $"P{drag.PreviewPitch}  T{drag.PreviewAbsoluteTick}", SystemFonts.MessageBoxFont,
            new Rectangle(Math.Min(bounds.Left, Math.Max(plot.Left, _lastMouse.X + 10)), Math.Max(plot.Top, _lastMouse.Y - 24), 150, 20),
            Color.White, TextFormatFlags.EndEllipsis);
    }

    private static Rectangle GetNoteBounds(Rectangle plot, ProjectDocument project, SongClip clip, NoteEvent note)
    {
        long absolute = clip.StartTick + note.StartTick;
        int x1 = TickToX(absolute, plot, project.Song.LengthTicks);
        int x2 = TickToX(absolute + Math.Max(1, note.DurationTicks), plot, project.Song.LengthTicks);
        int y1 = PitchToY(note.Pitch + 1, plot);
        int y2 = PitchToY(note.Pitch, plot);
        return new Rectangle(x1, y1 + 1, Math.Max(3, x2 - x1), Math.Max(3, y2 - y1 - 1));
    }

    private static int TickToX(long tick, Rectangle plot, long length) =>
        plot.Left + checked((int)Math.Round(Math.Clamp(tick / (double)Math.Max(1, length), 0, 1) * plot.Width));

    private static int PitchToY(int pitch, Rectangle plot)
    {
        const int low = 24;
        const int high = 109;
        double fraction = Math.Clamp((pitch - low) / (double)(high - low), 0, 1);
        return plot.Bottom - checked((int)Math.Round(fraction * plot.Height));
    }

    private sealed record DragState(
        string TrackId,
        string ClipId,
        string NoteId,
        long OriginalAbsoluteTick,
        int OriginalPitch,
        long TickOffset,
        int PitchOffset,
        long PreviewAbsoluteTick,
        int PreviewPitch);
}
