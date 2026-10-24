namespace TinySolitaire.Game;

public readonly record struct CardPlacement(Card Card, CardLocation Location, CardLocation VisualLocation, bool IsVisible);

public static class CardLayout
{
    public static bool BecomesCovered(CardPlacement before, CardPlacement after) =>
        before.IsVisible && !after.IsVisible
        && before.Card.Suit == after.Card.Suit && before.Card.Rank == after.Card.Rank
        && before.Location.Kind == after.Location.Kind && before.Location.Pile == after.Location.Pile;

    public static IEnumerable<CardPlacement> GetPlacements(GameState state)
    {
        for (int i = 0; i < state.Stock.Count; i++)
            yield return new(state.Stock[i], new(PileKind.Stock, 0, i), new(PileKind.Stock), i == state.Stock.Count - 1);

        int firstWaste = Math.Max(0, state.Waste.Count - state.DrawCount);
        int coveredWaste = Math.Max(0, firstWaste - 1);
        for (int i = 0; i < state.Waste.Count; i++)
            yield return new(state.Waste[i], new(PileKind.Waste, 0, i),
                new(PileKind.Waste, 0, Math.Max(i, coveredWaste)), i >= firstWaste);

        for (int pile = 0; pile < state.Foundations.Count; pile++)
            for (int i = 0; i < state.Foundations[pile].Count; i++)
                yield return new(state.Foundations[pile][i], new(PileKind.Foundation, pile, i),
                    new(PileKind.Foundation, pile, state.Foundations[pile].Count - 1), i == state.Foundations[pile].Count - 1);

        for (int pile = 0; pile < state.Tableau.Count; pile++)
            for (int i = 0; i < state.Tableau[pile].Count; i++)
            {
                CardLocation location = new(PileKind.Tableau, pile, i);
                yield return new(state.Tableau[pile][i], location, location, true);
            }
    }
}
