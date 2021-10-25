using System;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace SamplePlugin
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        // the below exist just to make saving less cumbersome

        [NonSerialized] private DalamudPluginInterface? _pluginInterface;

        public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
        public int Version { get; set; } = 0;

        public void Initialize( DalamudPluginInterface pluginInterface )
        {
            _pluginInterface = pluginInterface;
        }

        public void Save()
        {
            _pluginInterface!.SavePluginConfig( this );
        }
    }
}