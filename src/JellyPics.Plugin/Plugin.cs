using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyPics.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyPics.Plugin;

/// <summary>
/// JellyPics Upload Plugin - point d'entrée Jellyfin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "JellyPics Upload";
    public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    public override string Description =>
        "Plugin d'import photo/vidéo pour JellyPics. " +
        "Conserve les métadonnées temporelles lors de l'import.";

    public IEnumerable<PluginPageInfo> GetPages() =>
        [
            new PluginPageInfo
            {
                Name = "JellyPics",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.config.html"
            }
        ];
}
