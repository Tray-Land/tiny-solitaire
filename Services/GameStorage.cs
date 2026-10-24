using System.Diagnostics;
using System.Text.Json;
using TinySolitaire.Game;
using Windows.Storage;

namespace TinySolitaire.Services;

internal static class GameStorage
{
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    private static string SavePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "game.json");

    public static async Task<(SolitaireGame Game, string? Warning)> LoadAsync()
    {
        try
        {
            if (!File.Exists(SavePath))
                return (SolitaireGame.New(SettingsService.DrawCount), null);
            await using FileStream file = File.OpenRead(SavePath);
            GameDocument? document = await JsonSerializer.DeserializeAsync(file, GameJsonContext.Default.GameDocument);
            if (SolitaireGame.TryRestore(document, out SolitaireGame? game))
                return (game!, null);
            Debug.WriteLine("Saved game failed Klondike state validation.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Failed to load saved game: {ex}");
        }
        return (SolitaireGame.New(SettingsService.DrawCount), "Couldn't restore the saved deal. A new game is ready.");
    }

    public static async Task<string?> SaveAsync(GameDocument document)
    {
        await SaveGate.WaitAsync();
        try
        {
            string temporary = SavePath + ".tmp";
            await using (FileStream file = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(file, document, GameJsonContext.Default.GameDocument);
                await file.FlushAsync();
            }
            File.Move(temporary, SavePath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Failed to save game: {ex}");
            return "Couldn't save this game. Keep the app open and try another move to retry.";
        }
        finally
        {
            SaveGate.Release();
        }
    }
}
