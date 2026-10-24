using System.Text.Json.Serialization;

namespace TinySolitaire.Game;

public sealed class GameState
{
    public List<Card> Stock { get; set; } = [];
    public List<Card> Waste { get; set; } = [];
    public List<List<Card>> Foundations { get; set; } = [[], [], [], []];
    public List<List<Card>> Tableau { get; set; } = [[], [], [], [], [], [], []];
    public int DrawCount { get; set; } = 1;
    public int Moves { get; set; }
    public int Score { get; set; }
    public int Redeals { get; set; }
    public int ElapsedSeconds { get; set; }

    public GameState Clone() => new()
    {
        Stock = [.. Stock],
        Waste = [.. Waste],
        Foundations = Foundations.Select(p => p.ToList()).ToList(),
        Tableau = Tableau.Select(p => p.ToList()).ToList(),
        DrawCount = DrawCount,
        Moves = Moves,
        Score = Score,
        Redeals = Redeals,
        ElapsedSeconds = ElapsedSeconds,
    };
}

public sealed class GameDocument
{
    public int Version { get; set; } = 1;
    public GameState State { get; set; } = new();
    public List<Card> OriginalDeck { get; set; } = [];
    public List<GameState> History { get; set; } = [];
}

[JsonSerializable(typeof(GameDocument))]
public partial class GameJsonContext : JsonSerializerContext;
