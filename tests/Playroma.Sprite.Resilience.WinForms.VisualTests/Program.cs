using System.Reflection;
using Playroma.Sprite.Editor.WinForms;
using Playroma.Sprite.Resilience;

string root = Path.Combine(Path.GetTempPath(), "PlayromaSpritePhase10Visual-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    ApplicationConfiguration.Initialize();
    using MainForm form = new(new SpriteEditorIntegrationOptions(SettingsRoot: root));
    form.StartPosition = FormStartPosition.Manual;
    form.Bounds = new Rectangle(20, 20, 1400, 900);
    form.CreateControl();
    form.PerformLayout();

    TabControl tabs = FindAll<TabControl>(form).First(item => item.TabPages.Cast<TabPage>().Any(page => page.Text == "Resilience & Polish"));
    TabPage resilience = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "Resilience & Polish");
    tabs.SelectedTab = resilience;
    form.PerformLayout();

    Bitmap normal = Render(form);
    Assert(normal.Width == 1400 && normal.Height == 900, "Normal workspace rendered at the wrong size.");
    Assert(CountDistinctSamples(normal) >= 8, "Normal workspace visual output appears empty or unpainted.");

    MethodInfo mirror = RequiredMethod(typeof(MainForm), "SetInspectorMirrored");
    mirror.Invoke(form, [true]);
    form.PerformLayout();
    SpriteWorkspaceProfile mirrored = (SpriteWorkspaceProfile)RequiredMethod(typeof(MainForm), "CaptureWorkspaceProfile").Invoke(form, null)!;
    Assert(mirrored.MirrorInspector, "Mirrored inspector state was not captured.");
    Assert(mirrored.InspectorWidth >= 240, "Mirrored inspector width is invalid.");

    FieldInfo preferencesField = typeof(MainForm).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("preferences");
    SpriteEditorPreferences preferences = (SpriteEditorPreferences)preferencesField.GetValue(form)!;
    preferences.Theme = SpriteUiTheme.Dark;
    preferences.HighContrast = false;
    preferences.UiScale = 1.25d;
    preferences.FontScale = 1.1d;
    RequiredMethod(typeof(MainForm), "ApplyPreferences").Invoke(form, [preferences]);
    form.PerformLayout();

    Bitmap dark = Render(form);
    Assert(CountDistinctSamples(dark) >= 8, "Dark workspace visual output appears empty or unpainted.");
    Assert(AverageLuminance(dark) < AverageLuminance(normal), "Dark theme did not produce a darker visual surface.");
    Assert(form.MinimumSize.Width >= 1120 && form.MinimumSize.Height >= 720, "DPI/UI scaling did not update the minimum window size.");
    Assert(!string.IsNullOrWhiteSpace(form.AccessibleName), "Main editor accessibility name is missing.");

    Console.WriteLine("PASS Playroma Sprite Phase 10 WinForms visual-layout contracts");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL " + exception);
    return 1;
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static MethodInfo RequiredMethod(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(type.FullName, name);
static Bitmap Render(Control control)
{
    Bitmap bitmap = new(control.Width, control.Height);
    control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
    return bitmap;
}
static int CountDistinctSamples(Bitmap bitmap)
{
    HashSet<int> colors = [];
    for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 30))
        for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 40))
            colors.Add(bitmap.GetPixel(x, y).ToArgb());
    return colors.Count;
}
static double AverageLuminance(Bitmap bitmap)
{
    double total = 0; int count = 0;
    for (int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 30))
        for (int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 40))
        {
            Color color = bitmap.GetPixel(x, y);
            total += .2126d * color.R + .7152d * color.G + .0722d * color.B;
            count++;
        }
    return total / Math.Max(1, count);
}
static IEnumerable<T> FindAll<T>(Control root) where T : Control
{
    foreach (Control child in root.Controls)
    {
        if (child is T found) yield return found;
        foreach (T nested in FindAll<T>(child)) yield return nested;
    }
}
static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
