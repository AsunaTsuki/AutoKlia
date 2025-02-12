using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace AutoKlia;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string apikey { get; set; } = "";
    public bool ShowMannequinReadyOnly { get; set; } = false;
    // the below exist just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
