using System.Text.Json;
using System.Text.Json.Serialization;
using Penumbra.Game;

namespace Penumbra.Config;

public sealed class MonitorAction
{
    public bool PowerOff { get; set; }
    public int Brightness { get; set; } = 10;
}

public sealed class Preset
{
    public string Name { get; set; } = "";

    // Monitor fora do dicionário não é alterado pelo preset.
    public Dictionary<string, MonitorAction> Actions { get; set; } = new();
}

public sealed class AppConfig
{
    public string? MainMonitorId { get; set; }
    public List<Preset> Presets { get; set; } = new() { new Preset { Name = "Jogo" } };
    public string? ActivePresetName { get; set; } = "Jogo";

    // Brilho em % definido nos cartões, por ID de monitor; é o valor aplicado ao sair do modo jogo.
    public Dictionary<string, int> DefaultBrightness { get; set; } = new();
    public int ActivationSeconds { get; set; } = 5;
    public int RestoreSeconds { get; set; } = 10;
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }

    [JsonIgnore]
    public Preset? ActivePreset => Presets.FirstOrDefault(p => p.Name == ActivePresetName);

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this))!;
}

public static class AppPaths
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Penumbra");

    public static string Config => Path.Combine(Folder, "config.json");
    public static string State => Path.Combine(Folder, "state.json");
    public static string Log => Path.Combine(Folder, "app.log");
}

public static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            Log.Write($"Falha ao ler {path}: {e.Message}");
            return null;
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}

public sealed class FileSnapshotStore : ISnapshotStore
{
    public IReadOnlyList<MonitorSnapshot> Load() =>
        JsonFile.Read<List<MonitorSnapshot>>(AppPaths.State) ?? new List<MonitorSnapshot>();

    public void Save(IReadOnlyList<MonitorSnapshot> snapshots) => JsonFile.Write(AppPaths.State, snapshots);

    public void Clear() => File.Delete(AppPaths.State);
}

public static class Log
{
    private static readonly object Gate = new();

    public static bool Enabled { get; set; } = true;

    public static void Write(string message)
    {
        if (!Enabled)
            return;

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Folder);
                File.AppendAllText(AppPaths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        }
    }
}
