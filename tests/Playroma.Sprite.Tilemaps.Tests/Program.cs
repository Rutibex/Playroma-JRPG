using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.Tilemaps;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }

SpritePackageData package = SpritePackageData.CreateEmpty("Phase 8 Contract", 32, 32);
PixelBuffer source = new(16, 16);
PaintTile(source, 0, 0, new Rgba32(255, 0, 0));
PaintTile(source, 8, 0, new Rgba32(0, 255, 0));
PaintTile(source, 0, 8, new Rgba32(0, 0, 255));
PaintTile(source, 8, 8, new Rgba32(255, 255, 0));
SpriteTileset tileset = TilemapDocumentOperations.ImportTileset(package, source, new("Primary", 8, 8));
Check(tileset.Tiles.Count == 4, "Tileset split count is wrong.");
Check(tileset.Columns == 2 && tileset.Rows == 2, "Tileset grid dimensions are wrong.");
Check(tileset.Tiles.All(tile => tile.PixelContentId.HasValue), "Tiles were not backed by reusable pixel content.");
Check(package.Document.PixelContents.Count == 5, "Tile pixel descriptors were not added.");

SpriteLayer tilemap = TilemapDocumentOperations.AddTilemapLayer(package, tileset.Id, package.Document.Frames[0].Id, "Ground", 4, 4);
Check(tilemap.Kind == SpriteLayerKind.Tilemap, "Tilemap layer kind is wrong.");
Check(tilemap.TilemapCels.Count == 1, "Initial tilemap cel was not created.");
SpriteTilemapContent map = TilemapDocumentOperations.ContentFor(package.Document, tilemap, package.Document.Frames[0].Id);
Check(map.Columns == 4 && map.Rows == 4 && map.Cells.Count == 16, "Tilemap dimensions are wrong.");

TilemapDocumentOperations.Paint(package.Document, tilemap, package.Document.Frames[0].Id, 0, 0, new TileBrush([0]), SpriteTileEditMode.Manual);
TilemapDocumentOperations.Paint(package.Document, tilemap, package.Document.Frames[0].Id, 1, 0, new TileBrush([1], SpriteTileTransform.FlipHorizontal), SpriteTileEditMode.Manual);
TilemapDocumentOperations.Paint(package.Document, tilemap, package.Document.Frames[0].Id, 0, 1, new TileBrush([2], SpriteTileTransform.Rotate90), SpriteTileEditMode.Manual);
Check(map.GetCell(1, 0).Transform == SpriteTileTransform.FlipHorizontal, "Tile transform was not stored.");
PixelBuffer rendered = TilemapRasterizer.Render(package.Document, package.PixelBuffers, tilemap, tilemap.TilemapCels[0]);
Check(rendered.Width == 32 && rendered.Height == 32, "Tilemap raster dimensions are wrong.");
Check(rendered.GetPixel(1, 1).R == 255, "First tile rendered incorrectly.");
Check(rendered.GetPixel(9, 1).G == 255, "Second tile rendered incorrectly.");
Check(rendered.GetPixel(1, 9).B == 255, "Third tile rendered incorrectly.");

TilemapDocumentOperations.FillRectangle(package.Document, tilemap, package.Document.Frames[0].Id, new(2, 2, 2, 2), new TileBrush([3]));
Check(map.GetCell(3, 3).TileIndex == 3, "Rectangle fill failed.");
TilemapDocumentOperations.Fill(package.Document, tilemap, package.Document.Frames[0].Id, 3, 3, new TileBrush([0]));
Check(map.GetCell(2, 2).TileIndex == 0 && map.GetCell(3, 3).TileIndex == 0, "Flood fill failed.");
TilemapDocumentOperations.TransformRegion(package.Document, tilemap, package.Document.Frames[0].Id, new(2, 2, 2, 2), SpriteTileTransform.FlipVertical);
Check(map.GetCell(2, 2).Transform.HasFlag(SpriteTileTransform.FlipVertical), "Region transform failed.");
TilemapDocumentOperations.RemapTiles(package.Document, tileset.Id, new Dictionary<int, int> { [0] = 3 });
Check(map.GetCell(0, 0).TileIndex == 3, "Tile remapping failed.");
TilemapDocumentOperations.ResizeMap(package.Document, tilemap, package.Document.Frames[0].Id, 5, 3);
Check(map.Columns == 5 && map.Rows == 3 && map.Cells.Count == 15, "Tilemap resize failed.");

SpriteFrame second = SpriteAnimationOperations.InsertFrame(package, 1, FrameInsertionMode.CopyLinked, 0);
Check(tilemap.TilemapCels.Count == 2, "Linked frame did not receive a tilemap cel.");
Check(tilemap.TilemapCels[0].TilemapContentId == tilemap.TilemapCels[1].TilemapContentId, "Linked tilemap cel does not share content.");
TilemapDocumentOperations.UnlinkCel(package, tilemap, second.Id);
Check(tilemap.TilemapCels[0].TilemapContentId != tilemap.TilemapCels[1].TilemapContentId, "Unlink did not clone tilemap content.");

SpriteAutoTileRule rule = new() { NeighborMask = 0, TileIndex = 1 };
tileset.AutoRules.Add(rule);
SpriteTilemapContent secondMap = TilemapDocumentOperations.ContentFor(package.Document, tilemap, second.Id);
TilemapDocumentOperations.Paint(package.Document, tilemap, second.Id, 4, 2, new TileBrush([0]), SpriteTileEditMode.Automatic);
Check(secondMap.GetCell(4, 2).TileIndex is 0 or 1, "Automatic mode produced an invalid tile.");
TilemapDocumentOperations.Paint(package.Document, tilemap, second.Id, 4, 2, new TileBrush([0, 1, 2]), SpriteTileEditMode.Stack);
int stacked = secondMap.GetCell(4, 2).TileIndex;
TilemapDocumentOperations.Paint(package.Document, tilemap, second.Id, 4, 2, new TileBrush([0, 1, 2]), SpriteTileEditMode.Stack);
Check(secondMap.GetCell(4, 2).TileIndex != stacked, "Stack mode did not cycle tiles.");

SpriteLayer rasterLayer = TilemapDocumentOperations.RasterizeToImageLayer(package, tilemap, package.Document.Frames[0].Id);
Check(rasterLayer.Kind == SpriteLayerKind.Image && rasterLayer.Cels.Count == 1, "Rasterized image layer is invalid.");
Check(package.PixelBuffers[rasterLayer.Cels[0].PixelContentId].Width == 40, "Rasterized resized map width is wrong.");

SpriteSlice slice = SliceDocumentOperations.Add(package.Document, "Panel", new(0, 0, 16, 16), 0, new(8, 8), new(4, 4, 8, 8));
Check(slice.Keys.Count == 1, "Initial slice key was not created.");
SliceDocumentOperations.UpsertKey(package.Document, slice.Id, 1, new(2, 2, 16, 16), new(10, 10), new(6, 6, 8, 8));
Check(slice.Resolve(0).Bounds.X == 0 && slice.Resolve(1).Bounds.X == 2, "Animated slice keys resolve incorrectly.");
Check(slice.Resolve(100).FrameNumber == 1, "Slice did not retain its final key.");
string metadata = SliceDocumentOperations.ExportMetadata(package.Document);
Check(metadata.Contains("playroma-slices/1", StringComparison.Ordinal), "Slice metadata schema is missing.");
Check(metadata.Contains("Panel", StringComparison.Ordinal), "Slice name is missing from metadata.");
PixelBuffer nineSlice = NineSliceRenderer.Render(rendered, slice.Resolve(0), 31, 27);
Check(nineSlice.Width == 31 && nineSlice.Height == 27, "Nine-slice preview dimensions are wrong.");

SpriteAnimationOperations.InsertFrame(package, 1, FrameInsertionMode.Empty);
Check(slice.Keys.Any(key => key.FrameNumber == 2), "Slice keys did not shift when a frame was inserted.");
SpriteAnimationOperations.RemoveFrames(package, [1]);
Check(slice.Keys.All(key => key.FrameNumber < package.Document.Frames.Count), "Slice keys were not normalized after frame removal.");
SpriteAnimationOperations.MoveFrames(package, [0], 2);
Check(slice.Keys.Single(key => key.Bounds.X == 0).FrameNumber == 1 && slice.Keys.Single(key => key.Bounds.X == 2).FrameNumber == 0, "Slice keys did not follow frame identity during frame movement.");
SpriteAnimationOperations.ReverseFrames(package, 0, 1);
Check(slice.Keys.Single(key => key.Bounds.X == 0).FrameNumber == 0 && slice.Keys.Single(key => key.Bounds.X == 2).FrameNumber == 1, "Slice keys did not follow frame identity during range reversal.");

SpritePackageData geometryPackage = package.DeepClone();
SpritePackageOperations.ResizeSprite(geometryPackage, 64, 64, PixelResamplingMode.NearestNeighbor);
SpriteSlice scaledSlice = geometryPackage.Document.Slices.Single();
Check(scaledSlice.Resolve(0).Bounds == new IntRect(0, 0, 32, 32), "Sprite resize did not scale slice bounds.");
Check(scaledSlice.Resolve(0).Center == new IntRect(8, 8, 16, 16), "Sprite resize did not scale nine-slice geometry.");
SpritePackageOperations.ResizeCanvas(geometryPackage, new IntRect(4, 6, 48, 48));
Check(scaledSlice.Resolve(0).Bounds == new IntRect(-4, -6, 32, 32), "Canvas crop did not translate animated slice geometry.");
Check(geometryPackage.Document.Layers.All(layer => layer.Kind != SpriteLayerKind.Tilemap), "Canvas crop did not explicitly rasterize tilemap layers.");

string temp = Path.Combine(Path.GetTempPath(), "playroma-phase8-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
string packagePath = Path.Combine(temp, "phase8.playromasprite");
await new SpritePackageSerializer().SaveAsync(package, packagePath);
SpritePackageData roundTrip = await new SpritePackageSerializer().LoadPackageAsync(packagePath);
Check(roundTrip.Document.SchemaVersion == 2, "Phase 8 document schema version was not saved.");
Check(roundTrip.Document.Tilesets.Count == 1, "Tileset did not round-trip.");
Check(roundTrip.Document.Layers.Any(layer => layer.Kind == SpriteLayerKind.Tilemap), "Tilemap layer did not round-trip.");
Check(roundTrip.Document.TilemapContents.Count >= 2, "Tilemap contents did not round-trip.");
Check(roundTrip.Document.Slices.Count == 1 && roundTrip.Document.Slices[0].Keys.Count >= 1, "Animated slice did not round-trip.");
Check(SpriteDocumentValidator.Validate(roundTrip.Document).Count == 0, "Round-trip document failed validation.");

TileStudioBridgeRequest bridge = TileStudioBridge.Export(roundTrip, Path.Combine(temp, "bridge"), "Phase 8", 8, 8, "tileset", frameIndexes: [0]);
Check(File.Exists(bridge.ManifestPath), "Tile Studio bridge manifest was not created.");
Check(File.Exists(bridge.SourceImagePath), "Tile Studio bridge PNG was not created.");
Check(Directory.Exists(bridge.ReturnDirectory!), "Tile Studio return directory was not created.");
Check(File.ReadAllText(bridge.ManifestPath).Contains("playroma-sprite-tile-bridge/1", StringComparison.Ordinal), "Tile Studio manifest schema is wrong.");

Directory.Delete(temp, true);
Console.WriteLine($"Playroma Sprite tilemap, slice, and Tile Studio bridge tests passed: {checks} checks.");

static void PaintTile(PixelBuffer buffer, int x0, int y0, Rgba32 color)
{
    for (int y = y0; y < y0 + 8; y++) for (int x = x0; x < x0 + 8; x++) buffer.SetPixel(x, y, color);
}
