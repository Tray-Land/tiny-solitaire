namespace TinySolitaire.Game;

public readonly record struct DropTarget(CardLocation Location, double Left, double Top, double Width, double Height);

public static class DropTargetPolicy
{
    private const double EdgeTolerance = 12;

    public static CardLocation? Find(CardLocation source, double left, double top, double width, double height,
        IEnumerable<DropTarget> targets)
    {
        CardLocation? destination = null;
        double bestOverlap = 0;
        foreach (DropTarget target in targets)
        {
            if (target.Location.Kind is not (PileKind.Tableau or PileKind.Foundation)
                || (target.Location.Kind == source.Kind && target.Location.Pile == source.Pile))
                continue;
            double overlapWidth = Math.Min(left + width, target.Left + target.Width + EdgeTolerance)
                - Math.Max(left, target.Left - EdgeTolerance);
            double overlapHeight = Math.Min(top + height, target.Top + target.Height + EdgeTolerance)
                - Math.Max(top, target.Top - EdgeTolerance);
            double overlap = Math.Max(0, overlapWidth) * Math.Max(0, overlapHeight);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                destination = target.Location;
            }
        }
        return destination;
    }
}
