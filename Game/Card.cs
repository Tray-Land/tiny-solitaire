namespace TinySolitaire.Game;

public enum Suit { Clubs, Diamonds, Hearts, Spades }
public enum PileKind { Stock, Waste, Foundation, Tableau }

public readonly record struct Card(Suit Suit, int Rank, bool FaceUp = false)
{
    public bool IsRed => Suit is Suit.Diamonds or Suit.Hearts;
    public string RankText => Rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => Rank.ToString() };
    public string Symbol => Suit switch { Suit.Clubs => "\u2663", Suit.Diamonds => "\u2666", Suit.Hearts => "\u2665", _ => "\u2660" };
    public string Name => $"{Rank switch { 1 => "Ace", 11 => "Jack", 12 => "Queen", 13 => "King", _ => Rank.ToString() }} of {Suit.ToString().ToLowerInvariant()}";
    public Card Turn(bool faceUp) => this with { FaceUp = faceUp };
}

public readonly record struct CardLocation(PileKind Kind, int Pile = 0, int Index = 0);
public readonly record struct GameHint(CardLocation? Source, CardLocation? Target, string Message);
