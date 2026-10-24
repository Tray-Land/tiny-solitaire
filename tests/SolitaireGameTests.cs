using System.Text.Json;
using TinySolitaire.Game;

namespace TinySolitaire.Tests;

public class SolitaireGameTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void DealHas52UniqueCardsAndOnlyTopCardsFaceUp(int draw)
    {
        SolitaireGame game = SolitaireGame.New(draw, new Random(42));
        Assert.True(SolitaireGame.IsValid(game.State));
        Assert.Equal(24, game.State.Stock.Count);
        Assert.Equal(Enumerable.Range(1, 7), game.State.Tableau.Select(p => p.Count));
        Assert.All(game.State.Tableau, p => Assert.Single(p, c => c.FaceUp));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RecyclePreservesDrawOrderAndUndoRestoresIt(int draw)
    {
        SolitaireGame game = SolitaireGame.New(draw, new Random(42));
        game.Draw();
        Card[] firstDraw = game.State.Waste.ToArray();
        while (game.State.Stock.Count > 0)
            game.Draw();
        GameState before = game.State.Clone();
        Assert.True(game.Draw());
        Assert.Empty(game.State.Waste);
        Assert.Equal(24, game.State.Stock.Count);
        Assert.Equal(1, game.State.Redeals);
        Assert.True(game.Undo());
        Assert.Equal(before.Waste, game.State.Waste);
        game.Draw();
        game.Draw();
        Assert.Equal(firstDraw, game.State.Waste);
    }

    [Fact]
    public void DrawThreeHandlesPartialFinalPacket()
    {
        SolitaireGame game = Arrange([new(Suit.Hearts, 1)], [new(Suit.Clubs, 4)], draw: 3);
        int wasteCount = game.State.Waste.Count;
        game.Draw();
        Assert.Equal(wasteCount + 1, game.State.Waste.Count);
        Assert.Equal(new Card(Suit.Hearts, 1, true), game.State.Waste[^1]);
        Assert.Empty(game.State.Stock);
        Assert.True(SolitaireGame.IsValid(game.State));
    }

    [Fact]
    public void TableauRequiresDescendingAlternatingColorsAndKingsInEmptySlots()
    {
        SolitaireGame game = Arrange([], [new(Suit.Clubs, 7)], [new(Suit.Hearts, 6)], [new(Suit.Spades, 6)]);
        Assert.True(game.CanMove(new(PileKind.Tableau, 1), new(PileKind.Tableau, 0)));
        Assert.False(game.CanMove(new(PileKind.Tableau, 2), new(PileKind.Tableau, 0)));
        Assert.False(game.CanMove(new(PileKind.Tableau, 1), new(PileKind.Tableau, 6)));
        Assert.False(game.CanMove(new(PileKind.Tableau, 0), new(PileKind.Tableau, 0)));
        Assert.False(game.CanMove(new(PileKind.Stock), new(PileKind.Tableau, 0)));
    }

    [Fact]
    public void WholeRunsMoveAndRevealHiddenCardAndUndoRestoresAll()
    {
        SolitaireGame game = Arrange([], [new(Suit.Spades, 8)],
            [new(Suit.Clubs, 3, false), new(Suit.Hearts, 7), new(Suit.Clubs, 6)]);
        Card[] original = game.State.Tableau[1].ToArray();
        Assert.True(game.TryMove(new(PileKind.Tableau, 1, 1), new(PileKind.Tableau, 0)));
        Assert.Equal(3, game.State.Tableau[0].Count);
        Assert.True(game.State.Tableau[1][0].FaceUp);
        Assert.Equal(5, game.State.Score);
        Assert.True(game.Undo());
        Assert.Equal(original, game.State.Tableau[1]);
        Assert.Equal(0, game.State.Moves);
        Assert.Equal(0, game.State.Score);
    }

    [Fact]
    public void FoundationsRequireAceThenSameSuitAndOnlyOneCard()
    {
        SolitaireGame game = Arrange([], [new(Suit.Hearts, 1)], [new(Suit.Hearts, 2)], [new(Suit.Clubs, 2)]);
        Assert.True(game.TryAutoMove(new(PileKind.Tableau, 0)));
        Assert.False(game.CanMove(new(PileKind.Tableau, 2), new(PileKind.Foundation, 0)));
        Assert.True(game.TryMove(new(PileKind.Tableau, 1), new(PileKind.Foundation, 0)));
        Assert.Equal(20, game.State.Score);
    }

    [Fact]
    public void OnlyTopWasteCardCanMove()
    {
        SolitaireGame game = SolitaireGame.New(3, new Random(42));
        game.Draw();
        Assert.False(game.CanSelect(new(PileKind.Waste, 0, 0)));
        Assert.True(game.CanSelect(new(PileKind.Waste, 0, 2)));
        Assert.False(game.CanSelect(new(PileKind.Tableau, -1)));
    }

    [Fact]
    public void SaveRoundTripRetainsUndoDrawCountAndRestartDeal()
    {
        SolitaireGame game = SolitaireGame.New(3, new Random(42));
        GameState initial = game.State.Clone();
        game.Draw();
        game.State.ElapsedSeconds = 10;
        string json = JsonSerializer.Serialize(game.Export(), GameJsonContext.Default.GameDocument);
        Assert.True(SolitaireGame.TryRestore(JsonSerializer.Deserialize(json, GameJsonContext.Default.GameDocument), out var restored));
        Assert.True(restored!.Undo());
        Assert.Equal(initial.Stock, restored.State.Stock);
        Assert.Equal(10, restored.State.ElapsedSeconds);
        restored.Draw();
        restored.Restart();
        Assert.Equal(initial.Stock, restored.State.Stock);
        Assert.Equal(0, restored.State.ElapsedSeconds);
        Assert.Equal(3, restored.State.DrawCount);
        Assert.False(restored.CanUndo);
    }

    [Fact]
    public void InvalidSavesAreRejected()
    {
        GameDocument document = SolitaireGame.New().Export();
        document.State.Stock[0] = document.State.Stock[1];
        Assert.False(SolitaireGame.TryRestore(document, out _));
        document = SolitaireGame.New().Export();
        document.State.DrawCount = 2;
        Assert.False(SolitaireGame.TryRestore(document, out _));
        document = SolitaireGame.New().Export();
        document.State.Tableau[0][0] = document.State.Tableau[0][0].Turn(false);
        Assert.False(SolitaireGame.TryRestore(document, out _));
        Assert.False(SolitaireGame.TryRestore(null, out _));
    }

    [Fact]
    public void AutoFinishWinsAnExposedDeal()
    {
        GameDocument document = SolitaireGame.New().Export();
        document.State.Stock.Clear();
        document.State.Tableau = [[], [], [], [], [], [], []];
        for (int i = 0; i < 4; i++)
        {
            document.State.Foundations[i] = Enumerable.Range(1, 12).Select(r => new Card((Suit)i, r, true)).ToList();
            document.State.Tableau[i] = [new((Suit)i, 13, true)];
        }
        Assert.True(SolitaireGame.TryRestore(document, out var game));
        Assert.True(game!.CanAutoFinish);
        Assert.Equal(4, game.AutoFinish());
        Assert.True(game.IsWon);
        Assert.False(game.CanAutoFinish);
        Assert.True(game.Undo());
        Assert.False(game.IsWon);
    }

    [Fact]
    public void RandomLegalPlayMaintainsCardConservationAndUndoBound()
    {
        SolitaireGame game = SolitaireGame.New(3, new Random(22));
        for (int step = 0; step < 400; step++)
        {
            GameHint hint = game.FindHint();
            if (hint.Source is { } source && hint.Target is { } target)
                Assert.True(game.TryMove(source, target));
            else
                game.Draw();
            Assert.True(SolitaireGame.IsValid(game.State), $"Invalid after step {step}");
        }
        Assert.InRange(game.Export().History.Count, 1, SolitaireGame.UndoLimit);
    }

    private static SolitaireGame Arrange(List<Card> stock, List<Card> first, List<Card>? second = null,
        List<Card>? third = null, int draw = 1)
    {
        GameDocument document = SolitaireGame.New(draw).Export();
        // Parameters without explicit face-up flags represent exposed cards, except the first
        // card of a multi-card run, which is deliberately hidden for the reveal test.
        List<List<Card>> piles = [first, second ?? [], third ?? [], [], [], [], []];
        for (int i = 0; i < piles.Count; i++)
            piles[i] = piles[i].Select((c, j) => c.Turn(piles[i].Count < 3 || j > 0)).ToList();
        HashSet<(Suit, int)> used = stock.Concat(piles.SelectMany(p => p)).Select(c => (c.Suit, c.Rank)).ToHashSet();
        document.State.Stock = stock.Count > 0 ? stock : document.OriginalDeck.Where(c => !used.Contains((c.Suit, c.Rank))).ToList();
        document.State.Waste = stock.Count > 0 ? document.OriginalDeck.Where(c => !used.Contains((c.Suit, c.Rank))).Select(c => c.Turn(true)).ToList() : [];
        document.State.Tableau = piles;
        Assert.True(SolitaireGame.TryRestore(document, out var game));
        return game!;
    }
}
