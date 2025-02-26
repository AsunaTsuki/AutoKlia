using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using AutoKlia.Windows;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Net.Http;
using ECommons.EzIpcManager;
using System;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System.Linq;
using ECommons.ExcelServices;
using AutoKlia.Helpers;
using System.Threading.Tasks;
using System.Xml.Linq;
using ECommons.Logging;

namespace AutoKlia;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/autoklia";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("AutoKlia");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public const string EndpointURL = "https://klia.gamba.pro";
    //public const string EndpointURL = "http://localhost:5200";

    public string CurrentLocation = "";
    public string adminNameWorld = "";



    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();


        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });



        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

        // Adds another button that is doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [AutoKlia] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui


        if (Svc.ClientState.LocalPlayer?.IsTargetable == true)
        {
            adminNameWorld = Svc.ClientState.LocalPlayer.Name.ToString() + "@" + Svc.ClientState.LocalPlayer.HomeWorld.Value.Name.ToString();
            ToggleMainUI();
        }
        else
        {
            PluginLog.Information("Player is not loaded in.");
        }
        
    }
    
    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI()
    {
        _ = Task.Run(async () =>
        {
            await MainWindow.RefreshPayoutsAsync();
            await MainWindow.RefreshBalancesAsync();
            await MainWindow.RefreshTabsAsync();
        });


        MainWindow.Toggle();
    }

    
}
