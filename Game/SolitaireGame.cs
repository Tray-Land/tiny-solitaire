namespace TinySolitaire.Game;

public sealed class SolitaireGame
{
    public const int UndoLimit = 100;
    private readonly List<Card> _originalDeck;
    private readonly List<GameState> _history;

    private SolitaireGame(GameState state, List<Card> deck, List<GameState>? history = null)
    {
        State = state;
        _originalDeck = deck;
        _history = history ?? [];
    }

    public GameState State { get; private set; }
    public bool CanUndo => _history.Count > 0;
    public bool IsWon => State.Foundations.All(p => p.Count == 13);
    public bool CanAutoFinish => !IsWon && State.Stock.Count == 0 && State.Waste.Count == 0
        && State.Tableau.All(p => p.All(c => c.FaceUp));

    public static SolitaireGame New(int drawCount = 1, Random? random = null)
    {
        if (drawCount is not (1 or 3))
            throw new ArgumentOutOfRangeException(nameof(drawCount));
        List<Card> deck = Enum.GetValues<Suit>().SelectMany(s => Enumerable.Range(1, 13).Select(r => new Card(s, r))).ToList();
        random ??= Random.Shared;
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return new(Deal(deck, drawCount), deck);
    }

    private static GameState Deal(List<Card> deck, int drawCount)
    {
        GameState state = new() { DrawCount = drawCount };
        int next = 0;
        for (int pile = 0; pile < 7; pile++)
            for (int index = 0; index <= pile; index++)
                state.Tableau[pile].Add(deck[next++].Turn(index == pile));
        state.Stock = deck.Skip(next).Select(c => c.Turn(false)).ToList();
        return state;
    }

    public GameDocument Export() => new()
    {
        State = State.Clone(),
        OriginalDeck = [.. _originalDeck],
        History = _history.Select(s => s.Clone()).ToList(),
    };

    public static bool TryRestore(GameDocument? document, out SolitaireGame? game)
    {
        game = null;
        if (document is null || document.Version != 1 || !IsValid(document.State)
            || document.OriginalDeck is null || !IsDeck(document.OriginalDeck)
            || document.OriginalDeck.Any(c => c.FaceUp)
            || document.History is null || document.History.Count > UndoLimit
            || document.History.Any(s => !IsValid(s) || s.DrawCount != document.State.DrawCount))
            return false;
        game = new(document.State.Clone(), [.. document.OriginalDeck], document.History.Select(s => s.Clone()).ToList());
        return true;
    }

    public static bool IsValid(GameState? state)
    {
        if (state is null || state.DrawCount is not (1 or 3) || state.Moves < 0 || state.Score < 0 || state.Redeals < 0
            || state.ElapsedSeconds < 0 || state.Stock is null || state.Waste is null
            || state.Foundations is null || state.Foundations.Count != 4
            || state.Tableau is null || state.Tableau.Count != 7
            || state.Foundations.Any(p => p is null) || state.Tableau.Any(p => p is null))
            return false;
        List<Card> cards = [.. state.Stock, .. state.Waste, .. state.Foundations.SelectMany(p => p), .. state.Tableau.SelectMany(p => p)];
        if (!IsDeck(cards) || state.Stock.Any(c => c.FaceUp) || state.Waste.Any(c => !c.FaceUp))
            return false;
        foreach (List<Card> pile in state.Foundations)
            for (int i = 0; i < pile.Count; i++)
                if (!pile[i].FaceUp || pile[i].Rank != i + 1 || pile[i].Suit != pile[0].Suit)
                    return false;
        foreach (List<Card> pile in state.Tableau)
        {
            bool faceUp = false;
            for (int i = 0; i < pile.Count; i++)
            {
                if (!pile[i].FaceUp && faceUp)
                    return false;
                if (pile[i].FaceUp && faceUp && !CanStack(pile[i], pile[i - 1]))
                    return false;
                faceUp |= pile[i].FaceUp;
            }
            if (pile.Count > 0 && !pile[^1].FaceUp)
                return false;
        }
        return true;
    }

    private static bool IsDeck(List<Card> cards) => cards.Count == 52
        && cards.All(c => Enum.IsDefined(c.Suit) && c.Rank is >= 1 and <= 13)
        && cards.Select(c => (c.Suit, c.Rank)).Distinct().Count() == 52;

    private static bool CanStack(Card moving, Card onto) => moving.IsRed != onto.IsRed && moving.Rank == onto.Rank - 1;

    public bool CanSelect(CardLocation source)
    {
        List<Card>? pile = GetPile(source);
        if (pile is null || source.Index < 0 || source.Index >= pile.Count || !pile[source.Index].FaceUp)
            return false;
        if (source.Kind is PileKind.Waste or PileKind.Foundation)
            return source.Index == pile.Count - 1;
        return source.Kind == PileKind.Tableau;
    }

    public bool CanMove(CardLocation source, CardLocation target)
    {
        if (!CanSelect(source) || (source.Kind == target.Kind && source.Pile == target.Pile))
            return false;
        List<Card> from = GetPile(source)!;
        List<Card>? to = GetPile(target);
        if (to is null)
            return false;
        Card card = from[source.Index];
        return target.Kind switch
        {
            PileKind.Tableau => to.Count == 0 ? card.Rank == 13 : CanStack(card, to[^1]),
            PileKind.Foundation => source.Index == from.Count - 1
                && (to.Count == 0 ? card.Rank == 1 : card.Suit == to[^1].Suit && card.Rank == to[^1].Rank + 1),
            _ => false,
        };
    }

    public bool TryMove(CardLocation source, CardLocation target)
    {
        if (!CanMove(source, target))
            return false;
        Remember();
        List<Card> from = GetPile(source)!;
        List<Card> to = GetPile(target)!;
        to.AddRange(from.Skip(source.Index));
        from.RemoveRange(source.Index, from.Count - source.Index);
        if (target.Kind == PileKind.Foundation)
            State.Score += 10;
        else if (source.Kind == PileKind.Foundation)
            State.Score = Math.Max(0, State.Score - 15);
        else if (source.Kind == PileKind.Waste)
            State.Score += 5;
        if (source.Kind == PileKind.Tableau && from.Count > 0 && !from[^1].FaceUp)
        {
            from[^1] = from[^1].Turn(true);
            State.Score += 5;
        }
        State.Moves++;
        return true;
    }

    public bool TryAutoMove(CardLocation source)
    {
        for (int i = 0; i < 4; i++)
            if (TryMove(source, new(PileKind.Foundation, i)))
                return true;
        return false;
    }

    public bool Draw()
    {
        if (IsWon || (State.Stock.Count == 0 && State.Waste.Count == 0))
            return false;
        Remember();
        if (State.Stock.Count == 0)
        {
            State.Stock = State.Waste.AsEnumerable().Reverse().Select(c => c.Turn(false)).ToList();
            State.Waste.Clear();
            State.Redeals++;
            State.Score = Math.Max(0, State.Score - (State.DrawCount == 1 ? 100 : 20));
        }
        else
        {
            for (int i = 0; i < State.DrawCount && State.Stock.Count > 0; i++)
            {
                State.Waste.Add(State.Stock[^1].Turn(true));
                State.Stock.RemoveAt(State.Stock.Count - 1);
            }
        }
        State.Moves++;
        return true;
    }

    public bool Undo()
    {
        if (!CanUndo)
            return false;
        int elapsed = State.ElapsedSeconds;
        State = _history[^1];
        State.ElapsedSeconds = elapsed;
        _history.RemoveAt(_history.Count - 1);
        return true;
    }

    public void Restart()
    {
        State = Deal(_originalDeck, State.DrawCount);
        _history.Clear();
    }

    public int AutoFinish()
    {
        if (!CanAutoFinish)
            return 0;
        int moved = 0;
        bool progress;
        do
        {
            progress = false;
            for (int i = 0; i < 7; i++)
                if (State.Tableau[i].Count > 0 && TryAutoMove(new(PileKind.Tableau, i, State.Tableau[i].Count - 1)))
                {
                    moved++;
                    progress = true;
                }
        } while (progress);
        return moved;
    }

    public GameHint FindHint()
    {
        List<CardLocation> sources = [];
        if (State.Waste.Count > 0)
            sources.Add(new(PileKind.Waste, 0, State.Waste.Count - 1));
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < State.Tableau[i].Count; j++)
                if (State.Tableau[i][j].FaceUp)
                    sources.Add(new(PileKind.Tableau, i, j));
        foreach (CardLocation source in sources.AsEnumerable().Reverse())
            for (int i = 0; i < 4; i++)
            {
                CardLocation target = new(PileKind.Foundation, i);
                if (CanMove(source, target))
                    return new(source, target, $"Move {GetPile(source)![source.Index].Name} to a foundation.");
            }
        foreach (CardLocation source in sources)
            for (int i = 0; i < 7; i++)
            {
                CardLocation target = new(PileKind.Tableau, i);
                // Moving an entire king pile to another empty column doesn't reveal anything.
                if (source.Kind == PileKind.Tableau && source.Index == 0 && State.Tableau[i].Count == 0)
                    continue;
                if (CanMove(source, target))
                    return new(source, target, $"Move {GetPile(source)![source.Index].Name} to column {i + 1}.");
            }
        if (State.Stock.Count > 0)
            return new(null, new(PileKind.Stock), $"Draw {State.DrawCount} from the stock.");
        if (State.Waste.Count > 0)
            return new(null, new(PileKind.Stock), "Recycle the waste to look through the stock again.");
        return new(null, null, IsWon ? "Beautifully played. All 52 cards are home." : "No moves found. Try undo, or deal a new game.");
    }

    private List<Card>? GetPile(CardLocation location) => location.Kind switch
    {
        PileKind.Stock when location.Pile == 0 => State.Stock,
        PileKind.Waste when location.Pile == 0 => State.Waste,
        PileKind.Foundation when location.Pile is >= 0 and < 4 => State.Foundations[location.Pile],
        PileKind.Tableau when location.Pile is >= 0 and < 7 => State.Tableau[location.Pile],
        _ => null,
    };

    private void Remember()
    {
        if (_history.Count == UndoLimit)
            _history.RemoveAt(0);
        _history.Add(State.Clone());
    }
}
