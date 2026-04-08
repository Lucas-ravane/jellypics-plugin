using MediaBrowser.Model.Plugins;

namespace JellyPics.Plugin.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Dossier de destination pour la synchronisation.
    /// Doit être inclus dans une médiathèque Jellyfin.
    /// </summary>
    public string SyncTargetPath { get; set; } = string.Empty;
}
