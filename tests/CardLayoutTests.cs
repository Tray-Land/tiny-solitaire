using TinySolitaire.Game;

namespace TinySolitaire.Tests;

public class CardLayoutTests
{
    [Fact]
    public void LayoutTracksEveryCardIncludingHiddenCards()
    {
        SolitaireGame game = SolitaireGame.New(3, new Random(42));
        CardPlacement[] placements = CardLayout.GetPlacements(game.State).ToArray();
        Assert.Equal(52, placements.Length);
        Assert.Equal(52, placements.Select(p => (p.Card.Suit, p.Card.Rank)).Distinct().Count());
        Assert.Equal(29, placements.Count(p => p.IsVisible));
        Assert.All(placements.Where(p => p.Location.Kind == PileKind.Stock),
            p => Assert.Equal(new CardLocation(PileKind.Stock), p.VisualLocation));
        Assert.All(placements.Where(p => p.Location.Kind == PileKind.Tableau),
            p => Assert.Equal(p.Location, p.VisualLocation));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void DrawingAndUndoKeepCardIdentityAndLocateTheWholePacket(int draw)
    {
        SolitaireGame game = SolitaireGame.New(draw, new Random(42));
        CardPlacement[] before = CardLayout.GetPlacements(game.State).ToArray();
        game.Draw();
        CardPlacement[] after = CardLayout.GetPlacements(game.State).ToArray();
        CardPlacement[] waste = after.Where(p => p.Location.Kind == PileKind.Waste).ToArray();
        Assert.Equal(draw, waste.Length);
        foreach (CardPlacement card in waste)
        {
            Assert.True(card.IsVisible);
            CardPlacement origin = Assert.Single(before, p => p.Card.Suit == card.Card.Suit && p.Card.Rank == card.Card.Rank);
            Assert.Equal(PileKind.Stock, origin.VisualLocation.Kind);
            Assert.Equal(card.Location, card.VisualLocation);
        }
        Assert.True(game.Undo());
        Assert.Equal(before, CardLayout.GetPlacements(game.State));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void DrawingKeepsThePreviousWastePacketUnderTheIncomingCards(int draw)
    {
        SolitaireGame game = SolitaireGame.New(draw, new Random(42));
        game.Draw();
        CardPlacement[] before = CardLayout.GetPlacements(game.State).ToArray();
        game.Draw();
        var after = CardLayout.GetPlacements(game.State).ToDictionary(p => (p.Card.Suit, p.Card.Rank));
        CardPlacement[] previousWaste = before.Where(p => p.Location.Kind == PileKind.Waste && p.IsVisible).ToArray();
        Assert.Equal(draw, previousWaste.Length);
        Assert.All(previousWaste, p => Assert.True(CardLayout.BecomesCovered(p, after[(p.Card.Suit, p.Card.Rank)])));
        CardPlacement drawn = Assert.Single(before, p => p.Location.Kind == PileKind.Stock && p.IsVisible);
        Assert.False(CardLayout.BecomesCovered(drawn, after[(drawn.Card.Suit, drawn.Card.Rank)]));
    }

    [Theory]
    [InlineData(PileKind.Stock)]
    [InlineData(PileKind.Waste)]
    [InlineData(PileKind.Foundation)]
    public void OnlyCardsBuriedInTheirOwnPileAreRetained(PileKind kind)
    {
        Card card = new(Suit.Clubs, 1, true);
        CardPlacement before = new(card, new(kind), new(kind), true);
        Assert.True(CardLayout.BecomesCovered(before, before with { IsVisible = false }));
        Assert.False(CardLayout.BecomesCovered(before, before));
        Assert.False(CardLayout.BecomesCovered(before, before with { IsVisible = false, Location = new(kind, 1) }));
        Assert.False(CardLayout.BecomesCovered(before, before with { IsVisible = false, Location = new(PileKind.Tableau) }));
        Assert.False(CardLayout.BecomesCovered(before, before with { IsVisible = false, Card = new(Suit.Hearts, 1, true) }));
    }

    [Fact]
    public void BuriedCardsUseAvailableRenderedCardsAsAnchors()
    {
        GameState state = new()
        {
            DrawCount = 1,
            Waste = [new(Suit.Clubs, 5, true), new(Suit.Hearts, 4, true), new(Suit.Spades, 3, true)],
            Foundations = [[new(Suit.Clubs, 1, true), new(Suit.Clubs, 2, true)], [], [], []],
        };
        CardPlacement[] placements = CardLayout.GetPlacements(state).ToArray();
        Assert.All(placements.Where(p => p.Location.Kind == PileKind.Waste && p.Location.Index < 2),
            p => Assert.Equal(new CardLocation(PileKind.Waste, 0, 1), p.VisualLocation));
        Assert.All(placements.Where(p => p.Location.Kind == PileKind.Foundation),
            p => Assert.Equal(new CardLocation(PileKind.Foundation, 0, 1), p.VisualLocation));
        Assert.Equal(2, placements.Count(p => p.IsVisible));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void WasteRendersACoveredCardUnderItsVisibleFanButOnlyTheTopCardIsPlayable(int draw)
    {
        SolitaireGame game = SolitaireGame.New(draw, new Random(42));
        game.Draw();
        game.Draw();
        CardPlacement[] rendered = CardLayout.GetPlacements(game.State)
            .Where(p => p.Location.Kind == PileKind.Waste && p.Location == p.VisualLocation).ToArray();
        Assert.Equal(draw + 1, rendered.Length);
        Assert.Equal(draw, rendered.Count(p => p.IsVisible));
        CardPlacement underneath = rendered[0];
        Assert.False(underneath.IsVisible);
        Assert.Equal(draw - 1, underneath.Location.Index);
        Assert.All(rendered, p => Assert.Equal(p.Location.Index == game.State.Waste.Count - 1, game.CanSelect(p.Location)));
        CardPlacement next = Assert.Single(rendered, p => p.Location.Index == game.State.Waste.Count - 2);
        Assert.Equal(next.Location, next.VisualLocation);
    }

    [Fact]
    public void RecyclingMovesWasteAnchorsBackToTheStock()
    {
        SolitaireGame game = SolitaireGame.New(3, new Random(42));
        while (game.State.Stock.Count > 0)
            game.Draw();
        CardPlacement[] waste = CardLayout.GetPlacements(game.State).Where(p => p.Location.Kind == PileKind.Waste).ToArray();
        Assert.True(game.Draw());
        CardPlacement[] stock = CardLayout.GetPlacements(game.State).Where(p => p.Location.Kind == PileKind.Stock).ToArray();
        Assert.Equal(waste.Select(p => (p.Card.Suit, p.Card.Rank)).Order(),
            stock.Select(p => (p.Card.Suit, p.Card.Rank)).Order());
        Assert.All(stock, p => Assert.Equal(new CardLocation(PileKind.Stock), p.VisualLocation));
    }
}
