using Godot;

namespace StickFighter.Settings;

// The settings file: user://settings.cfg, read at startup and written whenever an option or binding
// changes. Scenarios never touch it, so a run is the same on any machine.
public static class Config
{
    public const string Path = "user://settings.cfg";

    public static UserSettings Current => DeviceInput.Settings;

    public static void Load()
    {
        if (FileAccess.FileExists(Path))
            DeviceInput.Settings = UserSettings.Read(FileAccess.GetFileAsString(Path), DeviceInput.Defaults);
        ApplyDisplay();
    }

    public static void Save()
    {
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PrintErr($"Couldn't save settings to {Path}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(Current.Write());
    }

    public static void ApplyDisplay()
    {
        var mode = Current.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
        DisplayServer.WindowSetVsyncMode(Current.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }
}
