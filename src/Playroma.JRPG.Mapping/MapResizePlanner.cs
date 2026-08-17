namespace Playroma.JRPG.Mapping;

public sealed record MapEventPosition(int Id, int X, int Y, string Name = "");

public sealed record MapEventRelocation(int Id, MapPoint Before, MapPoint? After, bool IsDeleted);

public sealed record MapResizePlan(
    int OldWidth,
    int OldHeight,
    int NewWidth,
    int NewHeight,
    int OffsetX,
    int OffsetY,
    int[] NewData,
    IReadOnlyList<MapEventRelocation> Events,
    MapRect PreservedSourceBounds,
    MapRect PreservedDestinationBounds)
{
    public int CroppedCellCount => checked(OldWidth * OldHeight - PreservedSourceBounds.Width * PreservedSourceBounds.Height);
    public int AddedCellCount => checked(NewWidth * NewHeight - PreservedDestinationBounds.Width * PreservedDestinationBounds.Height);
}

public sealed class MapResizePlanner
{
    public MapResizePlan CreatePlan(
        IReadOnlyMapBuffer map,
        int newWidth,
        int newHeight,
        MapResizeAnchor anchor,
        IReadOnlyList<MapEventPosition>? events = null,
        bool deleteCroppedEvents = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (newWidth <= 0) throw new ArgumentOutOfRangeException(nameof(newWidth));
        if (newHeight <= 0) throw new ArgumentOutOfRangeException(nameof(newHeight));
        (int offsetX, int offsetY) = CalculateOffset(map.Width, map.Height, newWidth, newHeight, anchor);
        int[] newData = new int[checked(newWidth * newHeight * map.LayerCount)];
        int sourceLeft = Math.Max(0, -offsetX);
        int sourceTop = Math.Max(0, -offsetY);
        int sourceRight = Math.Min(map.Width, newWidth - offsetX);
        int sourceBottom = Math.Min(map.Height, newHeight - offsetY);
        MapRect source = sourceRight <= sourceLeft || sourceBottom <= sourceTop
            ? default
            : new MapRect(sourceLeft, sourceTop, sourceRight - sourceLeft, sourceBottom - sourceTop);
        MapRect destination = source.IsEmpty
            ? default
            : new MapRect(source.Left + offsetX, source.Top + offsetY, source.Width, source.Height);
        for (int layer = 0; layer < map.LayerCount; layer++)
        {
            foreach (MapPoint point in source.Cells())
            {
                int targetX = point.X + offsetX;
                int targetY = point.Y + offsetY;
                int targetIndex = checked((layer * newHeight + targetY) * newWidth + targetX);
                newData[targetIndex] = map.Get(point.X, point.Y, layer);
            }
        }

        List<MapEventRelocation> relocations = [];
        foreach (MapEventPosition mapEvent in events ?? [])
        {
            MapPoint before = new(mapEvent.X, mapEvent.Y);
            MapPoint after = new(mapEvent.X + offsetX, mapEvent.Y + offsetY);
            bool inside = after.X >= 0 && after.X < newWidth && after.Y >= 0 && after.Y < newHeight;
            if (!inside && !deleteCroppedEvents)
            {
                relocations.Add(new MapEventRelocation(mapEvent.Id, before, null, false));
            }
            else
            {
                relocations.Add(new MapEventRelocation(mapEvent.Id, before, inside ? after : null, !inside));
            }
        }
        return new MapResizePlan(
            map.Width,
            map.Height,
            newWidth,
            newHeight,
            offsetX,
            offsetY,
            newData,
            relocations,
            source,
            destination);
    }

    public static (int X, int Y) CalculateOffset(
        int oldWidth,
        int oldHeight,
        int newWidth,
        int newHeight,
        MapResizeAnchor anchor)
    {
        int horizontal = newWidth - oldWidth;
        int vertical = newHeight - oldHeight;
        int x = anchor switch
        {
            MapResizeAnchor.Top or MapResizeAnchor.Centre or MapResizeAnchor.Bottom => horizontal / 2,
            MapResizeAnchor.TopRight or MapResizeAnchor.Right or MapResizeAnchor.BottomRight => horizontal,
            _ => 0
        };
        int y = anchor switch
        {
            MapResizeAnchor.Left or MapResizeAnchor.Centre or MapResizeAnchor.Right => vertical / 2,
            MapResizeAnchor.BottomLeft or MapResizeAnchor.Bottom or MapResizeAnchor.BottomRight => vertical,
            _ => 0
        };
        return (x, y);
    }
}
