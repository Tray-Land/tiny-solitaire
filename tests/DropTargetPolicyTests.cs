using TinySolitaire.Game;

namespace TinySolitaire.Tests;

public class DropTargetPolicyTests
{
    private static readonly CardLocation Source = new(PileKind.Waste);
    private static readonly CardLocation Tableau = new(PileKind.Tableau, 1);

    [Fact]
    public void CardOverlapCountsEvenWhenItsGrabPointMissesThePile()
    {
        DropTarget target = new(Tableau, 54, 100, 48, 72);
        Assert.Equal(Tableau, DropTargetPolicy.Find(Source, 20, 80, 48, 72, [target]));
    }

    [Theory]
    [InlineData(55, 0)]
    [InlineData(-55, 0)]
    [InlineData(0, 79)]
    [InlineData(0, -79)]
    public void NearMissesHaveTwelvePixelsOfEdgeTolerance(double left, double top)
    {
        DropTarget target = new(Tableau, 0, 0, 48, 72);
        Assert.Equal(Tableau, DropTargetPolicy.Find(Source, left, top, 48, 72, [target]));
    }

    [Theory]
    [InlineData(61, 0)]
    [InlineData(-61, 0)]
    [InlineData(0, 85)]
    [InlineData(0, -85)]
    public void DistantDropsDoNotSnapToAPile(double left, double top)
    {
        DropTarget target = new(Tableau, 0, 0, 48, 72);
        Assert.Null(DropTargetPolicy.Find(Source, left, top, 48, 72, [target]));
    }

    [Fact]
    public void LargestOverlapWinsBetweenNeighboringColumns()
    {
        CardLocation neighbor = new(PileKind.Tableau, 2);
        DropTarget[] targets = [new(Tableau, 54, 100, 48, 300), new(neighbor, 108, 100, 48, 300)];
        Assert.Equal(neighbor, DropTargetPolicy.Find(Source, 100, 150, 48, 72, targets));
        Assert.Equal(neighbor, DropTargetPolicy.Find(Source, 100, 150, 48, 72, targets.Reverse()));
    }

    [Fact]
    public void SpaceBelowTheStackBelongsToItsColumn()
    {
        DropTarget target = new(Tableau, 54, 100, 48, 300);
        Assert.Equal(Tableau, DropTargetPolicy.Find(Source, 54, 300, 48, 72, [target]));
    }

    [Fact]
    public void FoundationSlotsAndOccupiedFoundationsUseTheSameGeometry()
    {
        CardLocation foundation = new(PileKind.Foundation, 0);
        DropTarget target = new(foundation, 162, 0, 48, 72);
        Assert.Equal(foundation, DropTargetPolicy.Find(Source, 180, 50, 48, 72, [target]));
    }

    [Fact]
    public void SourcePileStockAndWasteAreNotDestinations()
    {
        DropTarget[] targets =
        [
            new(new(PileKind.Tableau, 1), 0, 0, 48, 300),
            new(new(PileKind.Stock), 0, 0, 48, 72),
            new(new(PileKind.Waste), 0, 0, 48, 72),
        ];
        Assert.Null(DropTargetPolicy.Find(new(PileKind.Tableau, 1, 3), 0, 0, 48, 72, targets));
    }

    [Fact]
    public void OnlyTheLeadingCardDeterminesTheTargetNotTheRestOfADraggedRun()
    {
        DropTarget target = new(new(PileKind.Foundation), 162, 0, 48, 72);
        Assert.Null(DropTargetPolicy.Find(Tableau, 162, 100, 48, 72, [target]));
    }
}
