using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Run();
            Console.WriteLine("PASS Phase 9C Enemy and Troop WinForms smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 9C WinForms smoke test: " + exception);
            return 1;
        }
    }

    private static void Run()
    {
        string root = FindRepositoryRoot();
        string source = Path.Combine(root, "testdata", "fixtures", "database-catalog-project");
        string temporary = Path.Combine(Path.GetTempPath(), "PlayromaJRPG-Phase9C-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, temporary);
        CreateTroopBattlerFixture(temporary);
        try
        {
            OperationResult<MvProjectSession> opened = MvProjectSession.OpenAsync(temporary, analysisLoadMode: ProjectAnalysisLoadMode.Deferred).AsTask().GetAwaiter().GetResult();
            MvProjectSession session = opened.Value ?? throw new InvalidOperationException(string.Join(Environment.NewLine, opened.Diagnostics.Select(item => item.Message)));
            try
            {
                SmokeCategory(session, MvDocumentKind.Enemies, 1);
                SmokeCategory(session, MvDocumentKind.Troops, 1);
            }
            finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { try { Directory.Delete(temporary, recursive: true); } catch { } }
    }

    private static void SmokeCategory(MvProjectSession session, MvDocumentKind kind, int recordId)
    {
        MvDatabaseFileSummary summary = session.Workspace.Snapshot.Database.Find(kind) ?? throw new InvalidOperationException($"{kind} fixture is missing.");
        IProjectDocumentLease lease = session.Workspace.AcquireDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult();
        try
        {
            MutableJsonProjectDocument document = lease.Document as MutableJsonProjectDocument ?? throw new InvalidOperationException($"{kind} did not load as mutable JSON.");
            IDatabaseEditorDefinition definition = DatabaseEditorRegistry.CreateBuiltIn().Get(kind);
            IDatabaseEditorBody body = kind switch
            {
                MvDocumentKind.Enemies => new EnemyEditorControl(session, document, definition),
                MvDocumentKind.Troops => new TroopEditorControl(session, document, definition),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            try
            {
                body.BindAsync([new DatabaseRecordHandle(kind, summary.RelativePath, recordId)]).AsTask().GetAwaiter().GetResult();
                if (body.View.IsDisposed || !body.View.Enabled) throw new InvalidOperationException($"{kind} editor did not bind.");
                if (kind == MvDocumentKind.Enemies)
                {
                    AssetIndexSelectionControl battler = EnumerateControls(body.View).OfType<AssetIndexSelectionControl>().Single();
                    battler.Hue = 141;
                    if (battler.Hue != 141) throw new InvalidOperationException("Enemy battler selector did not retain its hue value.");

                    using AssetIndexPickerDialog picker = new(
                        session,
                        Playroma.JRPG.Assets.AssetCategory.Enemies,
                        hasIndex: false,
                        current: new AssetIndexSelection(string.Empty, 0),
                        hasHue: true,
                        currentHue: 141);
                    picker.CreateControl();
                    TrackBar? hueBar = EnumerateControls(picker).OfType<TrackBar>().FirstOrDefault(control =>
                        string.Equals(control.AccessibleName, "Monster hue selection bar", StringComparison.Ordinal));
                    if (hueBar is null || hueBar.Minimum != 0 || hueBar.Maximum != 360 || hueBar.Value != 141)
                    {
                        throw new InvalidOperationException("Enemy graphic picker did not create the 0–360 hue selection bar.");
                    }
                }
                else if (kind == MvDocumentKind.Troops)
                {
                    TroopFormationCanvas canvas = EnumerateControls(body.View).OfType<TroopFormationCanvas>().Single();
                    if (canvas.LoadedImageCount != 1)
                    {
                        throw new InvalidOperationException($"Troop editor loaded {canvas.LoadedImageCount} battler images; expected the fixture battler to be visible.");
                    }
                    canvas.SelectOnly(0);
                    Application.DoEvents();
                    canvas.SelectOnly(null);
                    Application.DoEvents();

                    // Regression for Build Fix 30: the canvas borrows battler bitmaps from
                    // TroopEditorControl. A queued paint must tolerate a stale/disposed image
                    // while an asynchronous database rebind replaces those resources.
                    using (TroopFormationCanvas disposedImageCanvas = new() { Size = new Size(640, 480) })
                    {
                        disposedImageCanvas.CreateControl();
                        Bitmap stale = new(96, 96);
                        disposedImageCanvas.SetMembers(
                            [new MvTroopMemberSnapshot(0, 1, 320, 360, false, "{}")],
                            new Dictionary<int, string> { [1] = "Fixture Enemy" },
                            new Dictionary<int, Bitmap> { [0] = stale });
                        stale.Dispose();
                        if (disposedImageCanvas.LoadedImageCount != 1)
                        {
                            throw new InvalidOperationException("Troop canvas did not retain its owned battler copy after the source image was disposed.");
                        }
                        using Bitmap painted = new(640, 480);
                        disposedImageCanvas.DrawToBitmap(painted, new Rectangle(0, 0, painted.Width, painted.Height));
                        disposedImageCanvas.ClearImages();
                    }

                    bool hasEnemyPalette = EnumerateControls(body.View).Any(control =>
                        string.Equals(control.AccessibleName, "Enemy palette", StringComparison.Ordinal));
                    bool hasBattleCommands = EnumerateControls(body.View).Any(control =>
                        string.Equals(control.AccessibleName, "Battle event commands", StringComparison.Ordinal));
                    bool hasBattleBackgroundButton = EnumerateControls(body.View).OfType<Button>().Any(button =>
                        string.Equals(button.AccessibleName, "Change troop battle background", StringComparison.Ordinal));
                    if (!hasEnemyPalette || !hasBattleCommands || !hasBattleBackgroundButton)
                    {
                        throw new InvalidOperationException("The RPG Maker-style Troop workspace is missing its enemy palette, battle-event command list, or battleback selector.");
                    }
                }
                DatabaseFieldDescriptor name = definition.RecordSchema.FindField("name");
                DatabaseMutationService mutations = new(session.Editing);
                string original = document.GetString($"/{recordId}/name") ?? string.Empty;
                mutations.SetFieldAsync([new DatabaseRecordHandle(kind, summary.RelativePath, recordId)], name, original + " Smoke").AsTask().GetAwaiter().GetResult();
                session.Undo.UndoAsync().AsTask().GetAwaiter().GetResult();
                if (!string.Equals(original, document.GetString($"/{recordId}/name"), StringComparison.Ordinal)) throw new InvalidOperationException($"{kind} undo did not restore the original name.");
            }
            finally { body.View.Dispose(); }
        }
        finally { lease.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void CreateTroopBattlerFixture(string projectRoot)
    {
        string directory = Path.Combine(projectRoot, "img", "enemies");
        Directory.CreateDirectory(directory);
        string imagePath = Path.Combine(directory, "FixtureBattler.png");
        using (Bitmap bitmap = new(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            using SolidBrush body = new(Color.MediumPurple);
            using SolidBrush eye = new(Color.White);
            graphics.FillEllipse(body, 5, 8, 54, 50);
            graphics.FillEllipse(eye, 19, 24, 8, 8);
            graphics.FillEllipse(eye, 37, 24, 8, 8);
            bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        string enemiesPath = Path.Combine(projectRoot, "data", "Enemies.json");
        string json = File.ReadAllText(enemiesPath);
        json = json.Replace("\"battlerName\": \"\"", "\"battlerName\": \"FixtureBattler\"", StringComparison.Ordinal);
        File.WriteAllText(enemiesPath, json);
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in EnumerateControls(child)) yield return descendant;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Playroma.JRPG.sln.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
