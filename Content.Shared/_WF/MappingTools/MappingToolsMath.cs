using System.Numerics;

namespace Content.Shared._WF.MappingTools;

/// <summary>
/// Quarter-turn transforms of a selection, shared so the client preview matches what the server does.
/// Turns are clockwise; a selection keeps its centre when turned.
/// </summary>
public static class MappingToolsMath
{
    /// <summary>
    /// Normalises a turn count to 0-3.
    /// </summary>
    public static int Normalize(int turns)
    {
        return (turns % 4 + 4) % 4;
    }

    public static Vector2i RotatedSize(Vector2i size, int turns)
    {
        return Normalize(turns) % 2 == 0 ? size : new Vector2i(size.Y, size.X);
    }

    /// <summary>
    /// Rotates a cell relative to the bottom-left of a box of the given size.
    /// </summary>
    public static Vector2i RotateCell(Vector2i cell, Vector2i size, int turns)
    {
        for (var i = 0; i < Normalize(turns); i++)
        {
            cell = new Vector2i(cell.Y, size.X - 1 - cell.X);
            size = new Vector2i(size.Y, size.X);
        }

        return cell;
    }

    /// <summary>
    /// Rotates a position relative to the bottom-left of a box of the given size.
    /// </summary>
    public static Vector2 RotatePoint(Vector2 point, Vector2i size, int turns)
    {
        for (var i = 0; i < Normalize(turns); i++)
        {
            point = new Vector2(point.Y, size.X - point.X);
            size = new Vector2i(size.Y, size.X);
        }

        return point;
    }

    /// <summary>
    /// The rotation to add to an entity turned with its selection.
    /// </summary>
    public static Angle RotationDelta(int turns)
    {
        return Angle.FromDegrees(-90 * Normalize(turns));
    }

    /// <summary>
    /// Where the bottom-left of <paramref name="extent"/> lands after the move.
    /// </summary>
    public static Vector2i DestinationOrigin(Box2i extent, Vector2i offset, int turns)
    {
        var size = extent.Size;
        var rotated = RotatedSize(size, turns);
        var shift = new Vector2i(FloorDiv(size.X - rotated.X, 2), FloorDiv(size.Y - rotated.Y, 2));
        return extent.BottomLeft + shift + offset;
    }

    /// <summary>
    /// Maps a cell in <paramref name="extent"/> to where the move puts it.
    /// </summary>
    public static Vector2i TransformCell(Vector2i cell, Box2i extent, Vector2i offset, int turns)
    {
        return DestinationOrigin(extent, offset, turns) + RotateCell(cell - extent.BottomLeft, extent.Size, turns);
    }

    /// <summary>
    /// Maps a grid-local position in <paramref name="extent"/> to where the move puts it.
    /// </summary>
    public static Vector2 TransformPoint(Vector2 point, Box2i extent, Vector2i offset, int turns)
    {
        return DestinationOrigin(extent, offset, turns) + RotatePoint(point - extent.BottomLeft, extent.Size, turns);
    }

    /// <summary>
    /// Maps a box of cells inside <paramref name="extent"/> to where the move puts it.
    /// </summary>
    public static Box2i TransformBox(Box2i box, Box2i extent, Vector2i offset, int turns)
    {
        var a = TransformCell(box.BottomLeft, extent, offset, turns);
        var b = TransformCell(box.TopRight - Vector2i.One, extent, offset, turns);
        return new Box2i(Vector2i.ComponentMin(a, b), Vector2i.ComponentMax(a, b) + Vector2i.One);
    }

    /// <summary>
    /// The bottom-left cell for a paste of the given size centred on <paramref name="cursor"/>.
    /// </summary>
    public static Vector2i PasteOrigin(Vector2i cursor, Vector2i size, int turns)
    {
        var rotated = RotatedSize(size, turns);
        return cursor - new Vector2i(rotated.X / 2, rotated.Y / 2);
    }

    /// <summary>
    /// The cell a grid-local position falls in.
    /// </summary>
    public static Vector2i CellOf(Vector2 local)
    {
        return new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));
    }

    public static bool Contains(Box2i box, Vector2i cell)
    {
        return cell.X >= box.Left && cell.X < box.Right && cell.Y >= box.Bottom && cell.Y < box.Top;
    }

    /// <summary>
    /// The smallest box holding both corner cells.
    /// </summary>
    public static Box2i FromCorners(Vector2i a, Vector2i b)
    {
        return new Box2i(Vector2i.ComponentMin(a, b), Vector2i.ComponentMax(a, b) + Vector2i.One);
    }

    /// <summary>
    /// Grows <paramref name="box"/> to hold <paramref name="cell"/>; an empty box becomes that cell.
    /// </summary>
    public static Box2i Include(Box2i? box, Vector2i cell)
    {
        var single = new Box2i(cell, cell + Vector2i.One);
        if (box is not { } b)
            return single;

        return new Box2i(Vector2i.ComponentMin(b.BottomLeft, cell), Vector2i.ComponentMax(b.TopRight, cell + Vector2i.One));
    }

    private static int FloorDiv(int a, int b)
    {
        return (int) MathF.Floor(a / (float) b);
    }
}
