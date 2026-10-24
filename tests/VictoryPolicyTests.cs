using TinySolitaire.Game;

namespace TinySolitaire.Tests;

public class VictoryPolicyTests
{
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, false)]
    public void CelebrationOnlyStartsWhenAVisibleDealBecomesWon(bool wasWon, bool isWon, bool visible, bool expected)
    {
        Assert.Equal(expected, VictoryPolicy.ShouldCelebrate(wasWon, isWon, visible));
    }
}
