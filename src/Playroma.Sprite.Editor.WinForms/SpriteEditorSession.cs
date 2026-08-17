using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;
using Playroma.Sprite.Tilemaps;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class SpriteEditorSession
{
    private readonly SpritePackageSerializer serializer = new();

    public SpritePackageData Package { get; private set; } = SpritePackageData.CreateEmpty("New Sprite", 144, 192);
    public SpritePackageHistory History { get; } = new();
    public string? PackagePath { get; private set; }
    public string? ExportPath { get; set; }
    public bool IsDirty { get; private set; }
    public DateTimeOffset LastSavedAt { get; private set; }
    public Guid ActiveLayerId { get; private set; }
    public Guid SelectedLayerId { get; private set; }
    public Guid ActiveFrameId { get; private set; }
    public SelectionMask Selection { get; private set; } = new(144, 192);

    public SpriteDocument Document => Package.Document;
    public SpriteLayer ActiveLayer => SpriteLayerTree.GetRequired(Document, ActiveLayerId);
    public SpriteLayer SelectedLayer => SpriteLayerTree.GetRequired(Document, SelectedLayerId);
    public SpriteFrame ActiveFrame => Document.Frames.First(frame => frame.Id == ActiveFrameId);
    public int ActiveFrameIndex => Document.Frames.FindIndex(frame => frame.Id == ActiveFrameId);
    public SpriteCel ActiveCel => EnsureActiveCel();
    public SpritePixelContentDescriptor ActiveDescriptor => Document.PixelContents.First(item => item.Id == ActiveCel.PixelContentId);
    public PixelBuffer ActiveBuffer => Package.GetRequired(ActiveCel.PixelContentId);

    public event EventHandler? Changed;

    public SpriteEditorSession() => ResetActiveState();

    public void NewDocument(string name, int width, int height, string? profileId)
    {
        Package = SpritePackageData.CreateEmpty(name, width, height);
        Package.Document.Metadata.RpgMakerMvProfileId = profileId;
        PackagePath = null; ExportPath = null; History.Clear(); IsDirty = false; ResetActiveState(); RaiseChanged();
    }

    public void ImportPng(string path, string? profileId = null)
    {
        PngColorMetadata metadata = PngColorMetadataReader.Read(path);
        PixelBuffer imported = PngCodec.Load(path);
        Package = SpritePackageData.CreateEmpty(Path.GetFileNameWithoutExtension(path), imported.Width, imported.Height);
        Package.ActiveBuffer().CopyFrom(imported);
        Package.Document.Metadata.RpgMakerMvProfileId = profileId;
        Package.Document.ColorMode = metadata.ColorMode switch { PngExportColorMode.Indexed => SpriteColorMode.Indexed, PngExportColorMode.Grayscale => SpriteColorMode.Grayscale, _ => SpriteColorMode.Rgba };
        Package.Document.ColorProfile = metadata.IsSrgb ? "sRGB" : "Unspecified RGB";
        Package.Document.ColorProfileMetadata.Name = Package.Document.ColorProfile;
        Package.Document.ColorProfileMetadata.Kind = metadata.IsSrgb ? "srgb" : "unspecified";
        if (metadata.Gamma is double gamma && gamma > 0d) Package.Document.ColorProfileMetadata.Gamma = gamma;
        SpritePalette? palette = metadata.CreatePalette(Path.GetFileNameWithoutExtension(path) + " Palette");
        if (palette is not null) { Package.Document.Palettes.Add(palette); Package.Document.ActivePaletteId = palette.Id; Package.Document.TransparentPaletteIndex = Math.Max(0, palette.Entries.FindIndex(entry => entry.Color.A == 0)); }
        ExportPath = Path.GetFullPath(path); PackagePath = null; History.Clear(); IsDirty = false; ResetActiveState(); RaiseChanged();
    }

    public void ImportPngSequence(IEnumerable<string> paths, string? profileId = null, int defaultDurationMilliseconds = 100)
    {
        string[] ordered = paths.Select(Path.GetFullPath).ToArray();
        if (ordered.Length == 0) throw new ArgumentException("Choose at least one PNG frame.", nameof(paths));
        IReadOnlyList<AnimationFrameImage> frames = AnimationImageSequence.Load(ordered, defaultDurationMilliseconds);
        ReplaceWithAnimation(Path.GetFileNameWithoutExtension(ordered[0]), frames, profileId);
    }

    public void ImportGif(string path, string? profileId = null)
    {
        GifAnimation animation = GifCodec.Load(path);
        ReplaceWithAnimation(Path.GetFileNameWithoutExtension(path), animation.Frames, profileId);
        ExportPath = Path.GetFullPath(path);
    }

    public void ImportMvSheet(string path, RpgMakerMvImageProfile profile, int frameDurationMilliseconds = 100)
    {
        ArgumentNullException.ThrowIfNull(profile);
        PixelBuffer sheet = PngCodec.Load(path);
        SpritePackageData split = MvSheetExchange.SplitToAnimationPackage(sheet, profile, Path.GetFileNameWithoutExtension(path), frameDurationMilliseconds);
        ReplacePackage(split, packagePath: null, exportPath: Path.GetFullPath(path), dirty: false);
    }

    public PixelBuffer AssembleMvSheet(RpgMakerMvImageProfile profile, IReadOnlyCollection<int>? frameIndexes = null, MvSheetAssemblyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int[] indexes = NormalizeFrameSelection(frameIndexes);
        return MvSheetExchange.AssemblePackageFrames(Package, profile, indexes, options);
    }

    public void ReplaceWithStaticImage(string name, PixelBuffer pixels, string? profileId, bool dirty = true)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        SpritePackageData package = SpritePackageData.CreateEmpty(name, pixels.Width, pixels.Height);
        package.ActiveBuffer().CopyFrom(pixels);
        package.Document.Metadata.RpgMakerMvProfileId = profileId;
        ReplacePackage(package, packagePath: null, exportPath: null, dirty: dirty);
    }

    public void ReplacePackage(SpritePackageData package, string? packagePath = null, string? exportPath = null, bool dirty = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        SpriteDocumentValidator.ThrowIfInvalid(package.Document);
        Package = package;
        PackagePath = packagePath is null ? null : Path.GetFullPath(packagePath);
        ExportPath = exportPath is null ? null : Path.GetFullPath(exportPath);
        History.Clear();
        IsDirty = dirty;
        ResetActiveState();
        RaiseChanged();
    }

    public async Task OpenPackageAsync(string path, CancellationToken cancellationToken = default)
    {
        Package = await serializer.LoadPackageAsync(path, cancellationToken);
        PackagePath = Path.GetFullPath(path); ExportPath = Package.Document.Metadata.RuntimeRelativePath;
        History.Clear(); IsDirty = false; ResetActiveState(); RaiseChanged();
    }

    public async Task SaveAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        string target = path ?? PackagePath ?? throw new InvalidOperationException("A package path is required.");
        await serializer.SaveAsync(Package, target, cancellationToken);
        PackagePath = Path.GetFullPath(target); LastSavedAt = DateTimeOffset.Now; IsDirty = false; RaiseChanged();
    }

    public Task SaveSnapshotAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); return serializer.SaveAsync(Package, path, cancellationToken);
    }

    public PixelBuffer CompositeCurrentFrame(bool includeReferenceLayers = false) => CompositeFrame(ActiveFrameIndex, includeReferenceLayers);

    public PixelBuffer CompositeFrame(int frameIndex, bool includeReferenceLayers = false) =>
        AnimationFrameRenderer.RenderFrame(Package, frameIndex, includeReferenceLayers);

    public void ExportPng(string path)
    {
        PixelBuffer pixels = CompositeCurrentFrame();
        PngExportOptions options = Document.ColorMode switch
        {
            SpriteColorMode.Indexed when ActivePalette is SpritePalette palette => new(PngExportColorMode.Indexed, PaletteDocumentOperations.ToColors(palette), PaletteDitherMode.None, Document.TransparentPaletteIndex, WriteSrgbChunk: true, Gamma: Document.ColorProfileMetadata.Gamma),
            SpriteColorMode.Grayscale => new(PngExportColorMode.Grayscale, WriteSrgbChunk: true, Gamma: Document.ColorProfileMetadata.Gamma),
            _ => new(PngExportColorMode.Rgba, WriteSrgbChunk: true, Gamma: Document.ColorProfileMetadata.Gamma)
        };
        PngCodec.Save(pixels, path, options); ExportPath = Path.GetFullPath(path); RaiseChanged();
    }

    public IReadOnlyList<string> ExportPngSequence(string directory, string baseName, IReadOnlyCollection<int>? frameIndexes = null)
    {
        int[] indexes = NormalizeFrameSelection(frameIndexes);
        AnimationFrameImage[] frames = indexes.Select(index => new AnimationFrameImage(CompositeFrame(index), Document.Frames[index].DurationMilliseconds)).ToArray();
        IReadOnlyList<string> paths = AnimationImageSequence.Save(frames, directory, baseName);
        ExportPath = Path.GetFullPath(directory); RaiseChanged(); return paths;
    }

    public void ExportGif(string path, IReadOnlyCollection<int>? frameIndexes = null, int loopCount = 0)
    {
        int[] indexes = NormalizeFrameSelection(frameIndexes);
        AnimationFrameImage[] frames = indexes.Select(index => new AnimationFrameImage(CompositeFrame(index), Document.Frames[index].DurationMilliseconds)).ToArray();
        GifCodec.Save(frames, path, loopCount); ExportPath = Path.GetFullPath(path); RaiseChanged();
    }

    public void SetActiveLayer(Guid layerId)
    {
        SpriteLayer layer = SpriteLayerTree.GetRequired(Document, layerId);
        SelectedLayerId = layerId;
        ActiveLayerId = layer.Kind is SpriteLayerKind.Group or SpriteLayerKind.Tilemap
            ? Document.Layers.LastOrDefault(candidate => candidate.Kind is not SpriteLayerKind.Group and not SpriteLayerKind.Tilemap && (layer.Kind != SpriteLayerKind.Group || SpriteLayerTree.IsDescendant(Document, candidate.Id, layer.Id)))?.Id
                ?? Document.Layers.Last(candidate => candidate.Kind is not SpriteLayerKind.Group and not SpriteLayerKind.Tilemap).Id
            : layer.Id;
        RaiseChanged();
    }

    public void SetActiveFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= Document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        ActiveFrameId = Document.Frames[frameIndex].Id;
        Selection.Clear();
        RaiseChanged();
    }

    public void SetSelection(SelectionMask selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Width != Document.Canvas.Width || selection.Height != Document.Canvas.Height) throw new ArgumentException("Selection dimensions must match the canvas.", nameof(selection));
        Selection = selection; RaiseChanged();
    }

    public SpriteLayer AddLayer(SpriteLayerKind kind, string? name = null, Guid? parentId = null)
    {
        SpritePackageData before = Package.DeepClone();
        SpriteLayer layer = SpritePackageOperations.AddLayer(Package, name ?? DefaultLayerName(kind), kind, ActiveFrameId, parentId);
        ActiveLayerId = layer.Id; SelectedLayerId = layer.Id; CommitPackageEdit($"Add {kind} Layer", before); return layer;
    }

    public SpriteLayer DuplicateActiveLayer()
    {
        SpritePackageData before = Package.DeepClone(); SpriteLayer layer = SpritePackageOperations.DuplicateLayer(Package, SelectedLayerId);
        ActiveLayerId = layer.Id; SelectedLayerId = layer.Id; CommitPackageEdit("Duplicate Layer", before); return layer;
    }

    public void DeleteActiveLayer()
    {
        SpritePackageData before = Package.DeepClone(); Guid? selected = SpritePackageOperations.RemoveLayer(Package, SelectedLayerId);
        SelectedLayerId = selected ?? Document.Layers.First().Id; ActiveLayerId = SelectedLayerId; EnsurePaintLayerSelected(); CommitPackageEdit("Delete Layer", before);
    }

    public SpriteLayer GroupActiveLayer()
    {
        SpritePackageData before = Package.DeepClone();
        SpriteLayer active = SelectedLayer;
        SpriteLayer group = SpritePackageOperations.AddLayer(Package, "Group", SpriteLayerKind.Group, ActiveFrameId, active.ParentId);
        int activeIndex = Document.Layers.IndexOf(active);
        SpriteLayerTree.Move(Document, group.Id, activeIndex); SpriteLayerTree.Reparent(Document, active.Id, group.Id);
        SelectedLayerId = group.Id; ActiveLayerId = active.Kind == SpriteLayerKind.Group ? ActiveLayerId : active.Id; CommitPackageEdit("Group Layer", before); return group;
    }

    public void MoveActiveLayer(int direction)
    {
        if (direction == 0) return;
        SpritePackageData before = Package.DeepClone();
        if (!SpriteLayerTree.MoveSubtreeAmongSiblings(Document, SelectedLayerId, direction)) return;
        CommitPackageEdit(direction > 0 ? "Raise Layer" : "Lower Layer", before);
    }

    public void UpdateActiveLayer(string? name = null, bool? visible = null, bool? locked = null, byte? opacity = null, string? blendMode = null, bool? continuous = null)
    {
        SpriteLayer layer = SelectedLayer; SpritePackageData before = Package.DeepClone(); bool changed = false;
        if (name is not null && !string.Equals(layer.Name, name, StringComparison.Ordinal)) { layer.Name = string.IsNullOrWhiteSpace(name) ? "Layer" : name.Trim(); changed = true; }
        if (visible.HasValue && layer.Visible != visible.Value) { layer.Visible = visible.Value; changed = true; }
        if (locked.HasValue && layer.Locked != locked.Value) { layer.Locked = locked.Value; changed = true; }
        if (opacity.HasValue && layer.Opacity != opacity.Value) { layer.Opacity = opacity.Value; changed = true; }
        if (blendMode is not null && !string.Equals(layer.BlendMode, blendMode, StringComparison.OrdinalIgnoreCase)) { layer.BlendMode = blendMode; changed = true; }
        if (continuous.HasValue && layer.Continuous != continuous.Value) { layer.Continuous = continuous.Value; changed = true; }
        if (changed) CommitPackageEdit("Change Layer Properties", before);
    }

    public void MergeDown()
    {
        SpritePackageData before = Package.DeepClone(); SpriteLayer layer = SpritePackageOperations.MergeDown(Package, SelectedLayerId, ActiveFrameId);
        ActiveLayerId = layer.Id; SelectedLayerId = layer.Id; Selection.Clear(); CommitPackageEdit("Merge Down Across Frames", before);
    }

    public void Flatten(bool includeReferenceLayers = false)
    {
        SpritePackageData before = Package.DeepClone(); SpriteLayer layer = SpritePackageOperations.Flatten(Package, ActiveFrameId, includeReferenceLayers);
        ActiveLayerId = layer.Id; SelectedLayerId = layer.Id; Selection.Clear(); CommitPackageEdit("Flatten Animation", before);
    }

    public void ResizeSprite(int width, int height, PixelResamplingMode mode)
    {
        SpritePackageData before = Package.DeepClone(); SpritePackageOperations.ResizeSprite(Package, width, height, mode);
        Selection = new(width, height); CommitPackageEdit("Resize Sprite", before);
    }

    public void ResizeCanvas(IntRect sourceRegion)
    {
        SpritePackageData before = Package.DeepClone(); SpritePackageOperations.ResizeCanvas(Package, sourceRegion);
        Selection = new(sourceRegion.Width, sourceRegion.Height); CommitPackageEdit("Resize Canvas", before);
    }

    public bool TrimTransparent()
    {
        SpritePackageData before = Package.DeepClone();
        if (!SpritePackageOperations.TrimTransparent(Package, ActiveFrameId)) return false;
        Selection = new(Document.Canvas.Width, Document.Canvas.Height); CommitPackageEdit("Trim Transparent Edges", before); return true;
    }

    public SpriteFrame AddFrame(FrameInsertionMode mode = FrameInsertionMode.Empty)
    {
        SpritePackageData before = Package.DeepClone();
        int insertion = ActiveFrameIndex + 1;
        int? source = mode == FrameInsertionMode.Empty ? null : ActiveFrameIndex;
        SpriteFrame frame = SpriteAnimationOperations.InsertFrame(Package, insertion, mode, source, ActiveFrame.DurationMilliseconds);
        ActiveFrameId = frame.Id; Selection.Clear(); CommitPackageEdit(mode switch
        {
            FrameInsertionMode.Empty => "Add Empty Frame",
            FrameInsertionMode.CopyIndependent => "Add Copied Frame",
            _ => "Add Linked Frame"
        }, before);
        return frame;
    }

    public SpriteFrame DuplicateFrame(bool linked = false)
    {
        SpritePackageData before = Package.DeepClone();
        SpriteFrame frame = SpriteAnimationOperations.DuplicateFrame(Package, ActiveFrameIndex, linked);
        ActiveFrameId = frame.Id; Selection.Clear(); CommitPackageEdit(linked ? "Duplicate Linked Frame" : "Duplicate Frame", before); return frame;
    }

    public void RemoveFrames(IReadOnlyCollection<int> frameIndexes)
    {
        SpritePackageData before = Package.DeepClone(); ActiveFrameId = SpriteAnimationOperations.RemoveFrames(Package, frameIndexes);
        Selection.Clear(); CommitPackageEdit(frameIndexes.Count == 1 ? "Delete Frame" : "Delete Frames", before);
    }

    public void MoveFrames(IReadOnlyCollection<int> frameIndexes, int destinationIndex)
    {
        SpritePackageData before = Package.DeepClone(); Guid active = ActiveFrameId;
        SpriteAnimationOperations.MoveFrames(Package, frameIndexes, destinationIndex); ActiveFrameId = active;
        CommitPackageEdit("Move Frames", before);
    }

    public void ReverseFrames(int fromFrame, int toFrame)
    {
        SpritePackageData before = Package.DeepClone(); Guid active = ActiveFrameId;
        SpriteAnimationOperations.ReverseFrames(Package, fromFrame, toFrame); ActiveFrameId = active;
        CommitPackageEdit("Reverse Frames", before);
    }

    public void SetFrameDuration(IReadOnlyCollection<int> frameIndexes, int durationMilliseconds)
    {
        SpritePackageData before = Package.DeepClone(); SpriteAnimationOperations.SetFrameDurations(Document, frameIndexes, durationMilliseconds);
        CommitPackageEdit(frameIndexes.Count == 1 ? "Change Frame Duration" : "Change Frame Durations", before);
    }

    public void CopyCels(IReadOnlyCollection<Guid> layerIds, IReadOnlyCollection<int> sourceFrames, int destinationStart, bool linked)
    {
        SpritePackageData before = Package.DeepClone(); SpriteAnimationOperations.CopyCels(Package, layerIds, sourceFrames, destinationStart, linked);
        CommitPackageEdit(linked ? "Copy Linked Cels" : "Copy Cels", before);
    }

    public void MoveCels(IReadOnlyCollection<Guid> layerIds, IReadOnlyCollection<int> sourceFrames, int destinationStart)
    {
        SpritePackageData before = Package.DeepClone(); SpriteAnimationOperations.MoveCels(Package, layerIds, sourceFrames, destinationStart);
        CommitPackageEdit("Move Cels", before);
    }

    public void LinkCels(IReadOnlyCollection<CelSelection> selections)
    {
        SpritePackageData before = Package.DeepClone(); SpriteAnimationOperations.LinkCels(Package, selections);
        CommitPackageEdit("Link Cels", before);
    }

    public void UnlinkCels(IReadOnlyCollection<CelSelection> selections)
    {
        SpritePackageData before = Package.DeepClone(); SpriteAnimationOperations.UnlinkCels(Package, selections);
        CommitPackageEdit("Unlink Cels", before);
    }

    public void ClearCels(IReadOnlyCollection<CelSelection> selections)
    {
        SpritePackageData before = Package.DeepClone(); SpriteAnimationOperations.ClearCels(Package, selections);
        CommitPackageEdit("Clear Cels", before);
    }

    public SpriteAnimationTag AddTag(string name, int fromFrame, int toFrame, AnimationPlaybackDirection direction, string? color = null)
    {
        if (fromFrame < 0 || toFrame < fromFrame || toFrame >= Document.Frames.Count) throw new ArgumentOutOfRangeException(nameof(fromFrame));
        SpritePackageData before = Package.DeepClone();
        SpriteAnimationTag tag = new() { Name = UniqueTagName(name), FromFrame = fromFrame, ToFrame = toFrame, Direction = direction, Color = color };
        Document.Tags.Add(tag); CommitPackageEdit("Add Animation Tag", before); return tag;
    }

    public void UpdateTag(Guid tagId, string name, int fromFrame, int toFrame, AnimationPlaybackDirection direction, string? color = null)
    {
        SpritePackageData before = Package.DeepClone();
        SpriteAnimationTag tag = Document.Tags.FirstOrDefault(item => item.Id == tagId) ?? throw new KeyNotFoundException("The animation tag does not exist.");
        tag.Name = string.IsNullOrWhiteSpace(name) ? "Animation" : name.Trim(); tag.FromFrame = fromFrame; tag.ToFrame = toFrame; tag.Direction = direction; tag.Color = color;
        CommitPackageEdit("Edit Animation Tag", before);
    }

    public void DeleteTag(Guid tagId)
    {
        SpritePackageData before = Package.DeepClone();
        if (Document.Tags.RemoveAll(tag => tag.Id == tagId) == 0) return;
        CommitPackageEdit("Delete Animation Tag", before);
    }

    public SpriteLayer MoveSelectionToNewLayer(bool cutSource, bool useMergedSource = false)
    {
        if (Selection.IsEmpty) throw new InvalidOperationException("Select pixels before creating a layer from the selection.");
        if (cutSource && useMergedSource) throw new InvalidOperationException("A merged composite cannot be cut from its source layers.");
        if (cutSource && ActiveLayer.Locked) throw new InvalidOperationException("The active layer is locked.");
        SpritePackageData before = Package.DeepClone();
        PixelBuffer source = useMergedSource ? CompositeCurrentFrame() : ActiveBuffer;
        PixelClipboardPayload payload = PixelSelectionTransforms.Copy(source, Selection);
        if (cutSource) PixelSelectionTransforms.Clear(ActiveBuffer, Selection);
        Guid? parentId = SelectedLayer.Kind == SpriteLayerKind.Group ? SelectedLayer.Id : SelectedLayer.ParentId;
        SpriteLayer layer = SpritePackageOperations.AddLayer(Package, useMergedSource ? "Merged Selection" : "Selection", SpriteLayerKind.Image, ActiveFrameId, parentId);
        ActiveLayerId = layer.Id; SelectedLayerId = layer.Id;
        PixelSelectionTransforms.Paste(ActiveBuffer, payload, payload.Origin, true);
        CommitPackageEdit(useMergedSource ? "Copy Merged Selection to New Layer" : cutSource ? "Cut Selection to New Layer" : "Copy Selection to New Layer", before);
        return layer;
    }

    public SpriteLayer PasteToNewLayer(PixelClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        SpritePackageData before = Package.DeepClone();
        Guid? parentId = SelectedLayer.Kind == SpriteLayerKind.Group ? SelectedLayer.Id : SelectedLayer.ParentId;
        SpriteLayer layer = SpritePackageOperations.AddLayer(Package, "Pasted Selection", SpriteLayerKind.Image, ActiveFrameId, parentId);
        ActiveLayerId = layer.Id; SelectedLayerId = layer.Id;
        Selection = PixelSelectionTransforms.Paste(ActiveBuffer, payload, payload.Origin, true);
        CommitPackageEdit("Paste as New Layer", before);
        return layer;
    }

    public SpriteTileset ImportTileset(PixelBuffer sheet, TilesetImportOptions options)
    {
        SpritePackageData before = Package.DeepClone(); SpriteTileset tileset = TilemapDocumentOperations.ImportTileset(Package, sheet, options); CommitPackageEdit("Import Tileset", before); return tileset;
    }

    public SpriteLayer AddTilemapLayer(Guid tilesetId, string name = "Tilemap", int? columns = null, int? rows = null, Guid? parentId = null)
    {
        SpritePackageData before = Package.DeepClone(); SpriteLayer layer = TilemapDocumentOperations.AddTilemapLayer(Package, tilesetId, ActiveFrameId, name, columns, rows, parentId);
        SelectedLayerId = layer.Id; CommitPackageEdit("Add Tilemap Layer", before); return layer;
    }

    public SpriteTilemapCel EnsureSelectedTilemapCel(bool linkedFromPrevious = false)
    {
        SpriteLayer layer = SelectedLayer; if (layer.Kind != SpriteLayerKind.Tilemap) throw new InvalidOperationException("Select a tilemap layer first.");
        SpritePackageData before = Package.DeepClone(); SpriteTilemapCel cel = TilemapDocumentOperations.EnsureCel(Package, layer, ActiveFrameId, linkedFromPrevious); CommitPackageEdit("Create Tilemap Cel", before); return cel;
    }

    public void UnlinkSelectedTilemapCel()
    {
        SpriteLayer layer = SelectedLayer; if (layer.Kind != SpriteLayerKind.Tilemap) throw new InvalidOperationException("Select a tilemap layer first.");
        SpritePackageData before = Package.DeepClone(); TilemapDocumentOperations.UnlinkCel(Package, layer, ActiveFrameId); CommitPackageEdit("Unlink Tilemap Cel", before);
    }

    public SpriteLayer RasterizeSelectedTilemap()
    {
        SpriteLayer layer = SelectedLayer; if (layer.Kind != SpriteLayerKind.Tilemap) throw new InvalidOperationException("Select a tilemap layer first.");
        SpritePackageData before = Package.DeepClone(); SpriteLayer image = TilemapDocumentOperations.RasterizeToImageLayer(Package, layer, ActiveFrameId); ActiveLayerId = image.Id; SelectedLayerId = image.Id; CommitPackageEdit("Rasterize Tilemap Layer", before); return image;
    }

    public void CommitTilemapEdit(string description, SpritePackageData before) => CommitPackageEdit(description, before);
    public SpritePackageData CapturePackageSnapshot() => Package.DeepClone();

    public SpriteSlice AddSlice(string name, IntRect bounds, IntPoint? pivot = null, IntRect? center = null)
    {
        SpritePackageData before = Package.DeepClone(); SpriteSlice slice = SliceDocumentOperations.Add(Document, name, bounds, ActiveFrameIndex, pivot, center); CommitPackageEdit("Add Slice", before); return slice;
    }

    public void UpdateSlice(Guid sliceId, string name, IntRect bounds, IntPoint? pivot, IntRect? center, bool createFrameKey)
    {
        SpritePackageData before = Package.DeepClone(); SliceDocumentOperations.Rename(Document, sliceId, name);
        SpriteSlice slice = SliceDocumentOperations.GetRequired(Document, sliceId);
        int frame = createFrameKey || slice.Keys.Count == 0 ? ActiveFrameIndex : slice.Keys.OrderBy(key => Math.Abs(key.FrameNumber - ActiveFrameIndex)).First().FrameNumber;
        SliceDocumentOperations.UpsertKey(Document, sliceId, frame, bounds, pivot, center); CommitPackageEdit("Edit Slice", before);
    }

    public void RemoveSlice(Guid sliceId) { SpritePackageData before = Package.DeepClone(); SliceDocumentOperations.Remove(Document, sliceId); CommitPackageEdit("Delete Slice", before); }
    public void RemoveSliceKey(Guid sliceId, int frameNumber) { SpritePackageData before = Package.DeepClone(); SliceDocumentOperations.RemoveKey(Document, sliceId, frameNumber); CommitPackageEdit("Delete Slice Key", before); }

    public SpritePalette? ActivePalette => Document.ActivePaletteId is Guid id ? Document.Palettes.FirstOrDefault(palette => palette.Id == id) : null;

    public SpritePalette AddPalette(SpritePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Entries.Count is < 1 or > 256) throw new InvalidDataException("A palette must contain 1 to 256 colors.");
        SpritePackageData before = Package.DeepClone();
        string paletteJson = System.Text.Json.JsonSerializer.Serialize(palette, SpriteDocumentJsonSerializer.Options);
        SpritePalette clone = System.Text.Json.JsonSerializer.Deserialize<SpritePalette>(paletteJson, SpriteDocumentJsonSerializer.Options) ?? throw new InvalidDataException("Palette copy failed.");
        clone.Id = Guid.NewGuid();
        for (int index = 0; index < clone.Entries.Count; index++) clone.Entries[index].Index = index;
        Document.Palettes.Add(clone); Document.ActivePaletteId = clone.Id; RepairTransparentPaletteIndex(clone);
        CommitPackageEdit("Import Palette", before); return clone;
    }

    public SpritePalette GeneratePalette(string name, int maximumColors, PaletteDitherMode previewDither = PaletteDitherMode.None)
    {
        SpritePackageData before = Package.DeepClone(); SpritePalette palette = PaletteDocumentOperations.GeneratePalette(Package, name, maximumColors, previewDither);
        CommitPackageEdit("Generate Palette", before); return palette;
    }

    public void SetActivePalette(Guid paletteId)
    {
        if (Document.Palettes.All(palette => palette.Id != paletteId)) throw new KeyNotFoundException("Palette not found.");
        if (Document.ActivePaletteId == paletteId) return;
        SpritePackageData before = Package.DeepClone(); Document.ActivePaletteId = paletteId; RepairTransparentPaletteIndex(Document.Palettes.First(palette => palette.Id == paletteId)); CommitPackageEdit("Select Palette", before);
    }

    public void RemovePalette(Guid paletteId)
    {
        SpritePackageData before = Package.DeepClone(); SpritePalette? palette = Document.Palettes.FirstOrDefault(item => item.Id == paletteId);
        if (palette is null) return; Document.Palettes.Remove(palette);
        if (Document.ActivePaletteId == paletteId) Document.ActivePaletteId = Document.Palettes.FirstOrDefault()?.Id;
        if (Document.ColorMode == SpriteColorMode.Indexed && Document.Palettes.Count == 0) Document.ColorMode = SpriteColorMode.Rgba;
        RepairTransparentPaletteIndex();
        CommitPackageEdit("Delete Palette", before);
    }

    public void UpdatePaletteEntry(Guid paletteId, int index, SpriteColorValue color, string? name = null)
    {
        SpritePalette palette = Document.Palettes.First(item => item.Id == paletteId); SpritePaletteEntry entry = palette.Entries.First(item => item.Index == index);
        if (entry.Locked) throw new InvalidOperationException("The palette entry is locked.");
        SpritePackageData before = Package.DeepClone(); entry.Color = color; if (name is not null) entry.Name = name; if (Document.ActivePaletteId == paletteId) RepairTransparentPaletteIndex(palette); CommitPackageEdit("Edit Palette Color", before);
    }

    public void AddPaletteEntry(Guid paletteId, SpriteColorValue color)
    {
        SpritePalette palette = Document.Palettes.First(item => item.Id == paletteId); if (palette.Entries.Count >= 256) throw new InvalidOperationException("A palette cannot contain more than 256 colors.");
        SpritePackageData before = Package.DeepClone(); palette.Entries.Add(new SpritePaletteEntry { Index = palette.Entries.Count, Color = color }); if (Document.ActivePaletteId == paletteId) RepairTransparentPaletteIndex(palette); CommitPackageEdit("Add Palette Color", before);
    }

    public void RemovePaletteEntry(Guid paletteId, int index)
    {
        SpritePalette palette = Document.Palettes.First(item => item.Id == paletteId); if (palette.Entries.Count <= 1) throw new InvalidOperationException("A palette must contain at least one color.");
        SpritePaletteEntry entry = palette.Entries.First(item => item.Index == index); if (entry.Locked) throw new InvalidOperationException("The palette entry is locked.");
        SpritePackageData before = Package.DeepClone(); palette.Entries.Remove(entry); for (int i = 0; i < palette.Entries.Count; i++) palette.Entries[i].Index = i;
        RepairTransparentPaletteIndex(palette); CommitPackageEdit("Delete Palette Color", before);
    }

    public void ApplyActivePalette(PaletteDitherMode dither)
    {
        SpritePalette palette = ActivePalette ?? throw new InvalidOperationException("Choose or generate a palette first."); SpritePackageData before = Package.DeepClone();
        PaletteDocumentOperations.ApplyPalette(Package, palette, dither); CommitPackageEdit("Apply Indexed Palette", before);
    }

    public void ConvertColorMode(SpriteColorMode mode, int indexedColors = 256, PaletteDitherMode dither = PaletteDitherMode.None, GrayscaleMethod grayscaleMethod = GrayscaleMethod.Luminance)
    {
        if (Document.ColorMode == mode && mode != SpriteColorMode.Indexed) return;
        SpritePackageData before = Package.DeepClone();
        switch (mode)
        {
            case SpriteColorMode.Rgba: PaletteDocumentOperations.ConvertToRgba(Package); break;
            case SpriteColorMode.Grayscale: PaletteDocumentOperations.ConvertToGrayscale(Package, grayscaleMethod); break;
            case SpriteColorMode.Indexed:
                SpritePalette palette = ActivePalette ?? PaletteDocumentOperations.GeneratePalette(Package, $"{indexedColors}-Color Palette", indexedColors, dither);
                PaletteDocumentOperations.ApplyPalette(Package, palette, dither); break;
        }
        CommitPackageEdit($"Convert to {mode}", before);
    }

    public int ApplyBatchEffect(string description, BatchEffectSelection selection, Action<PixelBuffer> effect)
    {
        SpritePackageData before = Package.DeepClone(); int changed = BatchPixelEffects.Apply(Package, selection, effect);
        if (changed == 0) return 0; CommitPackageEdit(description, before); return changed;
    }

    public void AssignColorProfile(string profileId)
    {
        BuiltInColorProfile profile = ColorProfileOperations.GetRequired(profileId);
        if (string.Equals(Document.ColorProfileMetadata.Kind, profile.Kind, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(Document.ColorProfileMetadata.Gamma - profile.Gamma) < .000001d) return;
        SpritePackageData before = Package.DeepClone();
        ColorProfileOperations.Assign(Document, profile);
        CommitPackageEdit($"Assign {profile.DisplayName} Profile", before);
    }

    public int ConvertColorProfile(string profileId)
    {
        BuiltInColorProfile profile = ColorProfileOperations.GetRequired(profileId);
        SpritePackageData before = Package.DeepClone();
        int changed = ColorProfileOperations.Convert(Package, profile);
        CommitPackageEdit($"Convert to {profile.DisplayName}", before);
        return changed;
    }

    public void SortActivePalette(PaletteSortMode mode, bool descending = false)
    {
        SpritePalette palette = ActivePalette ?? throw new InvalidOperationException("Choose a palette before sorting colors.");
        SpritePackageData before = Package.DeepClone();
        PaletteSorting.Sort(palette, mode, descending);
        RepairTransparentPaletteIndex(palette);
        CommitPackageEdit($"Sort Palette by {mode}", before);
    }

    private void RepairTransparentPaletteIndex(SpritePalette? palette = null)
    {
        palette ??= ActivePalette;
        if (palette is null || palette.Entries.Count == 0) { Document.TransparentPaletteIndex = 0; return; }
        int transparent = palette.Entries.FindIndex(entry => entry.Color.A == 0);
        Document.TransparentPaletteIndex = transparent >= 0 ? transparent : Math.Clamp(Document.TransparentPaletteIndex, 0, palette.Entries.Count - 1);
    }

    public SpriteCel? FindCel(Guid layerId, int frameIndex)
    {
        SpriteLayer layer = SpriteLayerTree.GetRequired(Document, layerId);
        Guid frameId = Document.Frames[frameIndex].Id;
        return layer.Cels.FirstOrDefault(cel => cel.FrameId == frameId);
    }

    public void CommitPixelEdit(string description, PixelBuffer before)
    {
        if (!History.CommitPixel(description, Package, ActiveCel.PixelContentId, before)) return;
        IsDirty = true; RaiseChanged();
    }

    public void CommitPixelContentEdit(string description, Guid contentId, PixelBuffer before)
    {
        if (!History.CommitPixel(description, Package, contentId, before)) return;
        IsDirty = true; RaiseChanged();
    }

    public void CommitSelectionTransform(string description, PixelBuffer before, SelectionMask selection)
    {
        SelectionMask beforeSelection = Selection.Clone();
        Selection = selection;
        if (!History.CommitPixel(description, Package, ActiveCel.PixelContentId, before, beforeSelection, selection)) return;
        IsDirty = true; RaiseChanged();
    }

    public void MarkDirty() { IsDirty = true; RaiseChanged(); }

    public void Undo()
    {
        SpritePackageData? restored = History.Undo(Package); if (restored is null) return;
        Package = restored;
        if (History.LastRestoredSelection is SelectionMask restoredSelection) Selection = restoredSelection;
        RepairActiveState(); IsDirty = true; RaiseChanged();
    }

    public void Redo()
    {
        SpritePackageData? restored = History.Redo(Package); if (restored is null) return;
        Package = restored;
        if (History.LastRestoredSelection is SelectionMask restoredSelection) Selection = restoredSelection;
        RepairActiveState(); IsDirty = true; RaiseChanged();
    }

    private void ReplaceWithAnimation(string name, IReadOnlyList<AnimationFrameImage> frames, string? profileId)
    {
        if (frames.Count == 0) throw new InvalidDataException("The animation contains no frames.");
        int width = frames[0].Pixels.Width, height = frames[0].Pixels.Height;
        if (frames.Any(frame => frame.Pixels.Width != width || frame.Pixels.Height != height)) throw new InvalidDataException("Every animation frame must have identical dimensions.");
        SpritePackageData package = SpritePackageData.CreateEmpty(name, width, height);
        SpriteDocument document = package.Document;
        SpriteLayer layer = document.Layers[0];
        document.Frames.Clear(); document.PixelContents.Clear(); layer.Cels.Clear(); package.PixelBuffers.Clear();
        for (int index = 0; index < frames.Count; index++)
        {
            SpriteFrame frame = new() { Number = index, DurationMilliseconds = Math.Max(1, frames[index].DurationMilliseconds) };
            SpritePixelContentDescriptor descriptor = new() { Width = width, Height = height };
            document.Frames.Add(frame); document.PixelContents.Add(descriptor); package.PixelBuffers[descriptor.Id] = frames[index].Pixels.Clone();
            layer.Cels.Add(new SpriteCel { FrameId = frame.Id, PixelContentId = descriptor.Id });
        }
        if (frames.Count > 1) document.Tags.Add(new SpriteAnimationTag { Name = "Animation", FromFrame = 0, ToFrame = frames.Count - 1, Direction = AnimationPlaybackDirection.Forward });
        document.Metadata.RpgMakerMvProfileId = profileId;
        SpriteDocumentValidator.ThrowIfInvalid(document);
        Package = package; PackagePath = null; ExportPath = null; History.Clear(); IsDirty = false; ResetActiveState(); RaiseChanged();
    }

    private int[] NormalizeFrameSelection(IReadOnlyCollection<int>? frameIndexes)
    {
        int[] indexes = frameIndexes is null || frameIndexes.Count == 0
            ? Enumerable.Range(0, Document.Frames.Count).ToArray()
            : frameIndexes.Distinct().OrderBy(index => index).ToArray();
        if (indexes.Any(index => index < 0 || index >= Document.Frames.Count)) throw new ArgumentOutOfRangeException(nameof(frameIndexes));
        return indexes;
    }

    private string UniqueTagName(string name)
    {
        string root = string.IsNullOrWhiteSpace(name) ? "Animation" : name.Trim();
        string candidate = root; int suffix = 2;
        while (Document.Tags.Any(tag => string.Equals(tag.Name, candidate, StringComparison.OrdinalIgnoreCase))) candidate = $"{root} {suffix++}";
        return candidate;
    }

    private void CommitPackageEdit(string description, SpritePackageData before)
    {
        SpriteDocumentValidator.ThrowIfInvalid(Document); History.CommitPackage(description, before, Package); IsDirty = true; RepairActiveState(); RaiseChanged();
    }

    private SpriteCel EnsureActiveCel()
    {
        EnsurePaintLayerSelected(); SpriteLayer layer = SpriteLayerTree.GetRequired(Document, ActiveLayerId);
        SpriteCel? cel = layer.Cels.FirstOrDefault(item => item.FrameId == ActiveFrameId);
        if (cel is not null) return cel;
        if (layer.Continuous)
        {
            int activeIndex = ActiveFrameIndex;
            for (int index = activeIndex - 1; index >= 0; index--)
            {
                Guid priorFrameId = Document.Frames[index].Id;
                SpriteCel? prior = layer.Cels.FirstOrDefault(item => item.FrameId == priorFrameId);
                if (prior is null) continue;
                cel = new SpriteCel { FrameId = ActiveFrameId, PixelContentId = prior.PixelContentId, Position = prior.Position, Opacity = prior.Opacity, ZIndex = prior.ZIndex };
                layer.Cels.Add(cel); IsDirty = true; return cel;
            }
        }
        SpritePixelContentDescriptor descriptor = new() { Width = Document.Canvas.Width, Height = Document.Canvas.Height };
        Document.PixelContents.Add(descriptor); Package.PixelBuffers[descriptor.Id] = new PixelBuffer(descriptor.Width, descriptor.Height);
        cel = new SpriteCel { FrameId = ActiveFrameId, PixelContentId = descriptor.Id }; layer.Cels.Add(cel); IsDirty = true; return cel;
    }

    private void ResetActiveState()
    {
        ActiveFrameId = Document.Frames.OrderBy(frame => frame.Number).First().Id;
        ActiveLayerId = Document.Layers.LastOrDefault(layer => layer.Kind is not SpriteLayerKind.Group and not SpriteLayerKind.Tilemap)?.Id ?? Document.Layers.First().Id;
        SelectedLayerId = ActiveLayerId;
        Selection = new(Document.Canvas.Width, Document.Canvas.Height);
        EnsurePaintLayerSelected();
    }

    private void RepairActiveState()
    {
        if (Document.Frames.All(frame => frame.Id != ActiveFrameId)) ActiveFrameId = Document.Frames[0].Id;
        if (Document.Layers.All(layer => layer.Id != SelectedLayerId)) SelectedLayerId = Document.Layers.LastOrDefault(layer => layer.Kind is not SpriteLayerKind.Group and not SpriteLayerKind.Tilemap)?.Id ?? Document.Layers[0].Id;
        if (Document.Layers.All(layer => layer.Id != ActiveLayerId)) ActiveLayerId = SelectedLayerId;
        if (Selection.Width != Document.Canvas.Width || Selection.Height != Document.Canvas.Height) Selection = new(Document.Canvas.Width, Document.Canvas.Height);
        EnsurePaintLayerSelected();
    }

    private void EnsurePaintLayerSelected()
    {
        SpriteLayer current = SpriteLayerTree.GetRequired(Document, ActiveLayerId);
        if (current.Kind is not SpriteLayerKind.Group and not SpriteLayerKind.Tilemap) return;
        SpriteLayer? paint = current.Kind == SpriteLayerKind.Group
            ? Document.Layers.LastOrDefault(layer => layer.Kind is not SpriteLayerKind.Group and not SpriteLayerKind.Tilemap && SpriteLayerTree.IsDescendant(Document, layer.Id, current.Id))
            : null;
        paint ??= Document.Layers.LastOrDefault(layer => layer.Kind is not SpriteLayerKind.Group and not SpriteLayerKind.Tilemap);
        if (paint is null) paint = SpritePackageOperations.AddLayer(Package, "Layer 1", SpriteLayerKind.Image, ActiveFrameId);
        ActiveLayerId = paint.Id;
    }

    private static string DefaultLayerName(SpriteLayerKind kind) => kind switch
    {
        SpriteLayerKind.Group => "Group",
        SpriteLayerKind.Reference => "Reference",
        SpriteLayerKind.Background => "Background",
        SpriteLayerKind.Tilemap => "Tilemap",
        _ => "Layer"
    };

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

internal static class SpritePackageDataExtensions
{
    public static PixelBuffer ActiveBuffer(this SpritePackageData package)
    {
        SpritePixelContentDescriptor descriptor = package.Document.PixelContents.First(); return package.GetRequired(descriptor.Id);
    }
}
