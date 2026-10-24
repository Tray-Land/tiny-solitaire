using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TinySolitaire.Game;

namespace TinySolitaire.Views;

internal static class CardVisual
{
    public static readonly string[] StyleNames = ["Emoji garden", "Stargazer", "Fruit salad", "Cat nap"];
    private static readonly string[][] Patterns =
    [
        ["\U0001F33F", "\U0001F33C", "\U0001F340", "\U0001F344"],
        ["\u2B50", "\U0001F319", "\U0001FA90", "\u2728"],
        ["\U0001F352", "\U0001F34B", "\U0001F353", "\U0001F34A"],
        ["\U0001F431", "\U0001F43E", "\U0001F408", "\U0001F4A4"],
    ];

    public static Style Style(string name) => (Style)Application.Current.Resources[name];

    public static Border Front(Card card) => new()
    {
        Style = Style("PlayingCardBorderStyle"),
        Child = Face(card),
    };

    public static Border Back(int pattern)
    {
        Grid grid = new() { RowSpacing = 2, ColumnSpacing = 1 };
        for (int i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new());
        for (int i = 0; i < 3; i++)
            grid.ColumnDefinitions.Add(new());
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 3; column++)
            {
                TextBlock emoji = new()
                {
                    Text = Patterns[Math.Clamp(pattern, 0, 3)][(row + column) % 4],
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(emoji, row);
                Grid.SetColumn(emoji, column);
                grid.Children.Add(emoji);
            }
        return new() { Style = Style("CardBackStyle"), Child = grid, IsHitTestVisible = false };
    }

    public static Grid Face(Card card)
    {
        Grid face = new() { Padding = new Thickness(4, 2, 4, 3), Style = Style("CardFaceGridStyle") };
        Style ink = Style(card.IsRed ? "CardRedStyle" : "CardInkStyle");
        TextBlock rank = new()
        {
            Text = $"{card.RankText}{card.Symbol}",
            Style = ink,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        face.Children.Add(rank);
        Image suit = new()
        {
            Source = new SvgImageSource(new Uri($"ms-appx:///Assets/Suits/{card.Suit switch { Suit.Clubs => "Club", Suit.Diamonds => "Diamond", Suit.Hearts => "Heart", _ => "Spade" }}%20suit.svg")),
            Width = 29,
            Height = 29,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            IsHitTestVisible = false,
        };
        face.Children.Add(suit);
        TextBlock lower = new()
        {
            Text = card.RankText,
            Style = ink,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        face.Children.Add(lower);
        return face;
    }
}
