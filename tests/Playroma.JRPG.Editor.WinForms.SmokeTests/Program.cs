using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms;
using Playroma.JRPG.Editor.WinForms.Forms;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Documents;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Editor.WinForms.Eventing.CommandEditing;
using Playroma.JRPG.Editor.WinForms.ResourcesUI;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.RpgMakerMV.Documents;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.Editor.WinForms.Plugins;
using Playroma.JRPG.Editor.WinForms.Navigation;
using Playroma.JRPG.Documents.Mutable;
using System.Reflection;

namespace Playroma.JRPG.Editor.WinForms.SmokeTests;

internal static class Program
{
    // Phase 3 WinForms smoke test coverage is retained and extended by Phase 4.
    [STAThread]
    private static int Main()
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        string temp = Path.Combine(
            Path.GetTempPath(),
            "PlayromaJRPG-MultiWindow-Phase3-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            ShellServiceContainer services = BuildServices(temp);
            using EditorApplicationContext context = new(services, ["--safe-mode"]);
            ProjectHubForm hub = context.ProjectHub;
            hub.CreateControl();
            WaitForHubReady(context);
            if (hub.AutoScaleMode != AutoScaleMode.Dpi)
            {
                throw new InvalidOperationException("Project Hub is not DPI scaled.");
            }
            if (hub.MinimumSize.Width < 900 || hub.MinimumSize.Height < 600)
            {
                throw new InvalidOperationException("Project Hub minimum usable size is too small.");
            }
            if (hub.Controls.Count == 0)
            {
                throw new InvalidOperationException("Project Hub content was not created.");
            }

            SmokePhase4Composition(context);
            SmokePhase5Composition(temp);
            SmokeEventPageSettingsLayout();
            SmokeEventPagePreviewAnimationState();
            SmokeBranchInsertionPlanner();
            SmokePhase6Composition(context);
            SmokePhase7Composition(context);
            SmokePhase8Composition(context);
            SmokeCodeEditorHighlightingStability();
            SmokeCodeEditorTimerDisposal();
            SmokeWindowRegistry(context);
            hub.CloseFromController();
            Console.WriteLine("PASS Phase 8 multi-window utility, advanced editor, navigation, transaction, settings, registry, placement, and lifetime smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 8 WinForms smoke test: " + exception);
            return 1;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private static void SmokeCodeEditorHighlightingStability()
    {
        using CodeTextEditorControl editor = new();
        editor.CreateControl();
        editor.SetLanguage(CodeEditorLanguage.JavaScript);
        editor.Text = string.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(index => $"const value{index} = {index};"));
        RichTextBox innerEditor = editor.Controls.OfType<RichTextBox>().Single();

        int editAt = innerEditor.Text.IndexOf("value40", StringComparison.Ordinal);
        innerEditor.Select(editAt, "value40".Length);
        innerEditor.SelectedText = "answer40";
        int selectionStart = innerEditor.SelectionStart;
        int selectionLength = innerEditor.SelectionLength;

        // Let the delayed highlighter run. It must restore the caret/selection
        // after recolouring and must not generate another content modification.
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(400);
        while (DateTime.UtcNow < deadline)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(10);
        }

        if (innerEditor.SelectionStart != selectionStart || innerEditor.SelectionLength != selectionLength)
        {
            throw new InvalidOperationException("Syntax highlighting moved the code-editor caret or selection.");
        }
    }

    private static void SmokeCodeEditorTimerDisposal()
    {
        CodeTextEditorControl editor = new();
        editor.CreateControl();
        editor.SetLanguage(CodeEditorLanguage.JavaScript);
        editor.Text = "const answer = 42;";
        RichTextBox innerEditor = editor.Controls.OfType<RichTextBox>().Single();
        innerEditor.AppendText(" ");

        // A user edit schedules the delayed syntax-highlighting timer. Dispose
        // before the interval elapses, then pump messages past the old deadline.
        // A surviving timer would call into the disposed RichTextBox here.
        editor.Dispose();
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(400);
        while (DateTime.UtcNow < deadline)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static ShellServiceContainer BuildServices(string temp)
    {
        ShellServiceContainer services = new();
        JsonSettingsService settings = new(Path.Combine(temp, "settings.json"));
        RecentProjectService recent = new(Path.Combine(temp, "recent.json"));
        BackgroundTaskService tasks = new();
        NotificationService notifications = new();
        OutputService output = new();
        RecoveryService recovery = new(Path.Combine(temp, "recovery"));
        JsonProjectWorkspaceStateStore workspaceState = new(Path.Combine(temp, "workspace-state"));
        WorkspaceRecoveryContextBroker recoveryContext = new();
        ProjectFileWatchService fileWatcher = new();
        SelectionService selection = new();
        NavigationService navigation = new();
        WorkspaceService workspace = new(tasks, recent, notifications, output, recovery, recoveryContext);
        services.Add<ISettingsService>(settings);
        services.Add<IRecentProjectService>(recent);
        services.Add<IBackgroundTaskService>(tasks);
        services.Add<INotificationService>(notifications);
        services.Add<IOutputService>(output);
        services.Add<IRecoveryService>(recovery);
        services.Add<IProjectWorkspaceStateStore>(workspaceState);
        services.Add(recoveryContext);
        services.Add<IProjectFileWatchService>(fileWatcher);
        services.Add<ISelectionService>(selection);
        services.Add<INavigationService>(navigation);
        services.Add<IWorkspaceService>(workspace);
        return services;
    }


    private static void WaitForHubReady(EditorApplicationContext context)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (context.Controller.State == Playroma.JRPG.Editor.WinForms.Application.EditorApplicationState.Starting
            && DateTime.UtcNow < deadline)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(10);
        }
        if (context.Controller.State != Playroma.JRPG.Editor.WinForms.Application.EditorApplicationState.HubVisible)
        {
            throw new InvalidOperationException(
                $"Application did not reach HubVisible. Current state: {context.Controller.State}.");
        }
    }

    private static void SmokePhase4Composition(EditorApplicationContext context)
    {
        EditorWindowKind[] expected =
        [
            EditorWindowKind.EventEditor,
            EditorWindowKind.Database,
            EditorWindowKind.ResourceManager,
            EditorWindowKind.PluginManager,
            EditorWindowKind.RawJson,
            EditorWindowKind.ProjectSummary,
            EditorWindowKind.Validation,
            EditorWindowKind.MapProperties,
            EditorWindowKind.References,
            EditorWindowKind.Output,
            EditorWindowKind.BackgroundTasks,
            EditorWindowKind.Preferences,
            EditorWindowKind.RecoveredSessions
        ];
        foreach (EditorWindowKind kind in expected)
        {
            if (!context.WindowFactories.RegisteredKinds.Contains(kind))
            {
                throw new InvalidOperationException($"Phase 3 did not register the {kind} window factory.");
            }
        }

        if (!typeof(Playroma.JRPG.Editor.WinForms.Application.IProjectWorkspaceHost)
            .IsAssignableFrom(typeof(MapEditorForm)))
        {
            throw new InvalidOperationException("MapEditorForm does not implement the project workspace host contract.");
        }

        if (!typeof(EditorToolForm).IsAssignableFrom(typeof(EventEditorForm)))
        {
            throw new InvalidOperationException("EventEditorForm is not a dedicated editor tool form.");
        }

        DocumentRequest eventPageOne = new(
            EditorDocumentKind.EventEditor,
            "legacy-page-key",
            "data/Map001.json",
            MapId: 1,
            EventSurface: "MapEvent",
            EventId: 7,
            EventPageIndex: 0);
        DocumentRequest eventPageTwo = eventPageOne with { EventPageIndex = 1 };
        if (!string.Equals(
                EventWindowIdentity.Create(eventPageOne),
                EventWindowIdentity.Create(eventPageTwo),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Event page selection leaked into the stable Event Editor key.");
        }

        MapTileBrushSelection brush = new("B", 2, 2, [1, 2, 3, 4], 2, 1, 1);
        if (brush.PrimaryTileId != 1 || brush.TileIds.Count != 4)
        {
            throw new InvalidOperationException("Multi-tile map brush selection is not preserving its values.");
        }
    }

    private static void SmokePhase5Composition(string temp)
    {
        EventCommandRegistry registry = EventCommandRegistry.CreateBuiltIn();
        EventCommandDefinition[] selectable = registry.Commands.Where(command => command.IsInsertable).ToArray();
        if (selectable.Length != 105)
        {
            throw new InvalidOperationException($"Expected 105 selectable MV event commands, found {selectable.Length}.");
        }

        EventCommandPaletteLayout layout = EventCommandPaletteLayout.CreateRpgMakerMv();
        if (!selectable.Select(command => command.Code).Order().SequenceEqual(layout.AllCommandCodes.Order()))
        {
            throw new InvalidOperationException("The three-page Event Commands layout does not cover the selectable command registry exactly once.");
        }

        EventCommandUsageService usage = new(Path.Combine(temp, "event-command-usage.json"));
        using EventCommandPaletteForm palette = new(registry, usage, layout);
        palette.CreateControl();
        if (palette.Controls.Count == 0 || palette.AutoScaleMode != AutoScaleMode.Dpi)
        {
            throw new InvalidOperationException("The Event Commands palette did not create its DPI-aware control tree.");
        }

        using MovementRouteEditorDialog route = new(registry, MovementRouteModel.CreateDefault());
        route.CreateControl();
        if (route.Controls.Count == 0 || route.Result.Commands.Count == 0 || route.Result.Commands[^1].Code != 0)
        {
            throw new InvalidOperationException("Movement Route Studio did not preserve its command terminator.");
        }

        _ = new EventCommandEditorRegistry();
        _ = new EventCommandResultValidator(registry);
    }

    private static void SmokeEventPageSettingsLayout()
    {
        EventCommandRegistry registry = EventCommandRegistry.CreateBuiltIn();
        using EventPageSettingsControl settings = new(registry)
        {
            Size = new Size(390, 700)
        };
        settings.CreateControl();
        settings.PerformLayout();

        GroupBox[] groups = EnumerateControls(settings).OfType<GroupBox>().ToArray();
        if (groups.Length < 5)
        {
            throw new InvalidOperationException("Event page settings did not create all expected sections.");
        }
        if (groups.Any(group => group.Width < 280))
        {
            throw new InvalidOperationException("An event page settings section collapsed to an unusable width.");
        }

        Control[] textControls = EnumerateControls(settings)
            .Where(control => control is CheckBox or ComboBox or NumericUpDown or TextBox)
            .ToArray();
        if (textControls.Any(control => control.Width < 40))
        {
            throw new InvalidOperationException("An event page settings input collapsed to a one-character column.");
        }
    }

    private static void SmokeEventPagePreviewAnimationState()
    {
        EventCommandRegistry registry = EventCommandRegistry.CreateBuiltIn();
        using EventPageSettingsControl settings = new(registry);
        settings.CreateControl();

        FieldInfo? bitmapField = typeof(EventPageSettingsControl).GetField("_previewBitmap", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo? timerField = typeof(EventPageSettingsControl).GetField("_previewAnimationTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo? walkField = typeof(EventPageSettingsControl).GetField("_walk", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? updateMethod = typeof(EventPageSettingsControl).GetMethod("UpdateAnimationState", BindingFlags.Instance | BindingFlags.NonPublic);
        if (bitmapField is null || timerField is null || walkField is null || updateMethod is null)
        {
            throw new InvalidOperationException("Preview animation members were not found for smoke testing.");
        }

        bitmapField.SetValue(settings, new Bitmap(144, 192));
        CheckBox walk = (CheckBox)walkField.GetValue(settings)!;
        System.Windows.Forms.Timer timer = (System.Windows.Forms.Timer)timerField.GetValue(settings)!;

        walk.Checked = true;
        updateMethod.Invoke(settings, null);
        if (!timer.Enabled)
        {
            throw new InvalidOperationException("Event page preview animation did not start when walking animation was enabled.");
        }

        walk.Checked = false;
        updateMethod.Invoke(settings, null);
        if (timer.Enabled)
        {
            throw new InvalidOperationException("Event page preview animation did not stop when walking animation was disabled.");
        }
    }


    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in EnumerateControls(child)) yield return descendant;
        }
    }

    private static void SmokeBranchInsertionPlanner()
    {
        static RawEventCommand Command(int index, int code, int indent = 0) =>
            RawEventCommand.Parse(index, RawEventCommand.CreateJson(code, indent, "[]"));

        RawEventCommand opening = Command(0, 301);
        RawEventCommand win = Command(1, 601);
        RawEventCommand escape = Command(2, 602);
        RawEventCommand lose = Command(3, 603);
        RawEventCommand closing = Command(4, 604);
        RawEventCommand[] raw = [opening, win, escape, lose, closing];
        EventBlockNode battle = new(
            EventBlockKind.Battle,
            opening,
            [
                new EventBranchNode("Win", win, []),
                new EventBranchNode("Escape", escape, []),
                new EventBranchNode("Lose", lose, [])
            ],
            closing,
            new EventCommandSpan(0, raw.Length),
            raw);

        if (!EventCommandInsertionPlanner.TryResolveInsideBranch([battle], escape.RawIndex, closing.RawIndex, out EventCommandInsertionTarget target))
        {
            throw new InvalidOperationException("Battle Processing Escape branch did not resolve an insertion target.");
        }
        if (target.RawIndex != lose.RawIndex || target.Indent != 1)
        {
            throw new InvalidOperationException($"Escape branch insertion resolved to row {target.RawIndex}, indent {target.Indent}; expected row {lose.RawIndex}, indent 1.");
        }
    }

    private static void SmokePhase6Composition(EditorApplicationContext context)
    {
        if (!typeof(EditorToolForm).IsAssignableFrom(typeof(DatabaseForm)))
        {
            throw new InvalidOperationException("DatabaseForm is not a dedicated editor tool form.");
        }
        if (!context.WindowFactories.RegisteredKinds.Contains(EditorWindowKind.Database))
        {
            throw new InvalidOperationException("The dedicated Database window factory is not registered.");
        }

        DocumentRequest root = MvDocumentRequestFactory.CreateDatabaseRootRequest();
        if (!string.Equals(root.Identity, "database", StringComparison.Ordinal)
            || root.Kind != EditorDocumentKind.Database)
        {
            throw new InvalidOperationException("The Database root request does not use the stable singleton identity.");
        }

        DatabaseNavigationHistory history = new();
        DatabaseNavigationTarget actors = new("Actors", Playroma.JRPG.RpgMakerMV.MvDocumentKind.Actors, 1);
        DatabaseNavigationTarget items = new("Items", Playroma.JRPG.RpgMakerMV.MvDocumentKind.Items, 2);
        history.Visit(actors);
        history.Visit(items);
        if (!history.CanGoBack || history.GoBack() != actors || !history.CanGoForward)
        {
            throw new InvalidOperationException("Database-local Back/Forward navigation is not preserving targets.");
        }

        EditorSettings settings = new();
        if (settings.Version < 5 || settings.EffectiveDatabaseWindowLayout.SelectedCategory != "Actors")
        {
            throw new InvalidOperationException("Database window settings schema was not initialized.");
        }
    }

    private static void SmokePhase7Composition(EditorApplicationContext context)
    {
        if (!typeof(EditorToolForm).IsAssignableFrom(typeof(ResourceManagerForm)))
        {
            throw new InvalidOperationException("ResourceManagerForm is not a dedicated editor tool form.");
        }
        if (!context.WindowFactories.RegisteredKinds.Contains(EditorWindowKind.ResourceManager))
        {
            throw new InvalidOperationException("The dedicated Resource Manager window factory is not registered.");
        }

        EditorSettings settings = new();
        if (settings.Version < 6
            || settings.EffectiveResourceManagerLayout.SelectedCategory != "All"
            || settings.EffectiveResourceManagerLayout.ViewMode != "Details")
        {
            throw new InvalidOperationException("Resource Manager window settings schema was not initialized.");
        }

        ResourceNavigationTarget target = new(
            Playroma.JRPG.Assets.AssetCategory.Characters,
            "Actor1",
            PreviewTab: "Variants");
        ResourceNavigationTarget restored = ResourceNavigationTarget.FromLocation(target.ToLocation());
        if (restored.Category != target.Category
            || restored.LogicalName != target.LogicalName
            || restored.PreviewTab != target.PreviewTab)
        {
            throw new InvalidOperationException("Resource Manager navigation target did not round-trip.");
        }
    }

    private static void SmokePhase8Composition(EditorApplicationContext context)
    {
        Type[] dedicatedForms =
        [
            typeof(PluginManagerForm),
            typeof(ValidationForm),
            typeof(ReferenceExplorerForm),
            typeof(OutputForm),
            typeof(BackgroundTasksForm),
            typeof(RawJsonForm),
            typeof(ProjectSummaryForm),
            typeof(PreferencesForm)
        ];
        if (dedicatedForms.Any(type => !typeof(EditorToolForm).IsAssignableFrom(type)))
        {
            throw new InvalidOperationException("A Phase 8 utility is not a dedicated EditorToolForm.");
        }

        EditorWindowKind[] expected =
        [
            EditorWindowKind.PluginManager,
            EditorWindowKind.Validation,
            EditorWindowKind.References,
            EditorWindowKind.Output,
            EditorWindowKind.BackgroundTasks,
            EditorWindowKind.RawJson,
            EditorWindowKind.ProjectSummary,
            EditorWindowKind.Preferences,
            EditorWindowKind.RecoveredSessions
        ];
        foreach (EditorWindowKind kind in expected)
        {
            if (!context.WindowFactories.RegisteredKinds.Contains(kind))
                throw new InvalidOperationException($"The Phase 8 {kind} factory is not registered.");
        }

        EditorSettings settings = new();
        if (settings.Version < 7
            || !settings.EffectiveReferenceExplorerWindowLayout.FollowSelection
            || !settings.EffectiveOutputWindowLayout.AutoScroll
            || !settings.EffectivePreferences.ConfirmAdvancedRawJsonEditing)
        {
            throw new InvalidOperationException("Phase 8 utility settings schema was not initialized.");
        }

        PluginMetadata metadata = MvPluginMetadataParser.Parse(
            "SmokePlugin",
            "/*:\n * @plugindesc Smoke description\n * @author Playroma\n * @param Enabled\n * @type boolean\n * @default true\n */");
        if (metadata.Description != "Smoke description"
            || metadata.Author != "Playroma"
            || !metadata.Parameters.TryGetValue("Enabled", out PluginParameterMetadata? parameter)
            || parameter.Type != "boolean"
            || parameter.DefaultValue != "true")
        {
            throw new InvalidOperationException("MV plugin header metadata was not parsed without execution.");
        }

        ReferenceTarget target = new(ReferenceTargetKind.Actor, 17);
        NavigationLocation? location = EditorNavigationCoordinator.FromReferenceTarget(target);
        if (location?.Document.Kind != EditorDocumentKind.Database || location.RecordId != 17)
        {
            throw new InvalidOperationException("Reference target navigation did not resolve Actor #17.");
        }

        if (!typeof(ITransactionalDocumentSnapshot).IsAssignableFrom(typeof(MvPluginManifestDocument)))
        {
            throw new InvalidOperationException("plugins.js does not implement transactional snapshots.");
        }

        OutputEntry entry = new(
            DateTimeOffset.UtcNow,
            "Smoke",
            "Structured output",
            Playroma.JRPG.Core.DiagnosticSeverity.Warning,
            "Detail",
            location,
            Guid.NewGuid());
        if (entry.Severity != Playroma.JRPG.Core.DiagnosticSeverity.Warning
            || entry.Location is null
            || entry.RelatedTaskId is null)
        {
            throw new InvalidOperationException("Structured OutputEntry fields were not preserved.");
        }
    }

    private static void SmokeWindowRegistry(EditorApplicationContext context)
    {
        int factoryCalls = 0;
        List<TrackingLifetime> lifetimes = [];
        context.WindowFactories.Register(
            EditorWindowKind.Custom,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                factoryCalls++;
                TrackingLifetime lifetime = new();
                lifetimes.Add(lifetime);
                SmokeEditorToolForm window = new(request, lifetime);
                window.MinimumSize = new Size(420, 280);
                return ValueTask.FromResult<IWinFormsEditorWindow>(window);
            });

        WindowRequest firstRequest = new(
            EditorWindowKind.Custom,
            "sample-editor",
            DisplayTitle: "Sample Editor",
            Scope: WindowScope.Application,
            PlacementId: "Smoke.SampleEditor");
        IEditorWindow first = context.WindowService.OpenAsync(firstRequest).AsTask().GetAwaiter().GetResult();
        IEditorWindow duplicate = context.WindowService.OpenAsync(
            firstRequest with { Identity = "SAMPLE-EDITOR" }).AsTask().GetAwaiter().GetResult();

        if (!ReferenceEquals(first, duplicate))
        {
            throw new InvalidOperationException("Duplicate window request created a second Form.");
        }
        if (factoryCalls != 1)
        {
            throw new InvalidOperationException("Factory was called more than once for one stable key.");
        }
        if (context.WindowService.OpenWindows.Count != 1)
        {
            throw new InvalidOperationException("Window registry count is incorrect.");
        }
        if (!ReferenceEquals(context.WindowService.ActiveWindow, first))
        {
            throw new InvalidOperationException("Active window was not tracked.");
        }
        if (!context.WindowService.CanCloseScopeAsync(WindowScope.Application).AsTask().GetAwaiter().GetResult())
        {
            throw new InvalidOperationException("Application-window preflight unexpectedly failed.");
        }

        IWinFormsEditorWindow concrete = (IWinFormsEditorWindow)first;
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        concrete.Form.WindowState = FormWindowState.Normal;
        concrete.Form.Bounds = new Rectangle(work.Left + 40, work.Top + 40, 720, 480);
        bool closed = context.WindowService.CloseAsync(first).AsTask().GetAwaiter().GetResult();
        if (!closed) throw new InvalidOperationException("Window close was unexpectedly rejected.");
        if (!lifetimes[0].Disposed)
        {
            throw new InvalidOperationException("Window-owned async lifetime was not released.");
        }
        if (context.WindowService.OpenWindows.Count != 0)
        {
            throw new InvalidOperationException("Closed window remained in the registry.");
        }

        IEditorWindow reopened = context.WindowService.OpenAsync(firstRequest).AsTask().GetAwaiter().GetResult();
        IWinFormsEditorWindow reopenedConcrete = (IWinFormsEditorWindow)reopened;
        if (reopenedConcrete.Form.Width != 720 || reopenedConcrete.Form.Height != 480)
        {
            throw new InvalidOperationException(
                $"Saved window placement was not restored. Got {reopenedConcrete.Form.Width}x{reopenedConcrete.Form.Height}.");
        }
        context.WindowService.CloseScopeForcedAsync(WindowScope.Application).AsTask().GetAwaiter().GetResult();
        if (!lifetimes[1].Disposed)
        {
            throw new InvalidOperationException("Reopened window lifetime was not released.");
        }
    }

    private sealed class SmokeEditorToolForm : EditorToolForm
    {
        private readonly TrackingLifetime _lifetime;

        public SmokeEditorToolForm(WindowRequest request, TrackingLifetime lifetime) : base(request)
        {
            _lifetime = lifetime;
        }

        protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallContent(new Label
            {
                Text = "Dedicated Phase 12 smoke editor",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            });
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnDisposeWindowAsync() => _lifetime.DisposeAsync();
    }

    private sealed class TrackingLifetime : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
