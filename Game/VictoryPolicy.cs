namespace TinySolitaire.Game;

public static class VictoryPolicy
{
    public static bool ShouldCelebrate(bool wasWon, bool isWon, bool isVisible) => isVisible && !wasWon && isWon;
}
