using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LocalIntrosExtended.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LocalIntrosExtended
{
    public class LocalIntrosPlugin : BasePlugin<IntroPluginConfiguration>, IHasWebPages
    {
        public override string Name => "Local Intros Extended";

        public override Guid Id => Guid.Parse("5b2c93cf-d5a2-4a7b-a25e-04f76269df91");

        public const int DefaultResolution = 1080;

        public static LocalIntrosPlugin Instance { get; private set; }

        public static ILibraryManager LibraryManager { get; private set; }

        public LocalIntrosPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILibraryManager libraryManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            LibraryManager = libraryManager;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
            };
        }
    }
}
