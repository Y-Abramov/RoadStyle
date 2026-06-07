using System;
using Topomatic.ApplicationPlatform.Plugins;

namespace RoadStyle
{
    public class PluginHost : PluginHostInitializator
    {
        protected override Type[] GetTypes()
        {
            return new[] { typeof(RoadStylePlugin) };
        }

        public override void Initialize(PluginFactory factory)
        {
            base.Initialize(factory);
        }
    }
}
