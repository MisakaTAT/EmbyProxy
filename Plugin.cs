using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using System;

namespace EmbyProxy
{
    public sealed class Plugin : BasePluginSimpleUI<PluginOptions>, IDisposable
    {
        private static readonly Guid PluginId = new Guid("6a0fa5b3-07fb-44ea-a3e6-e5e47a0c7df9");

        private readonly ProxyPatch _proxyPatch;

        public static Plugin Instance { get; private set; }

        public ILogger Logger { get; }

        public Plugin(IApplicationHost applicationHost, ILogManager logManager)
            : base(applicationHost)
        {
            Instance = this;
            Logger = logManager.GetLogger(Name);
            _proxyPatch = new ProxyPatch(Logger);
            _proxyPatch.TryApply();
        }

        public override string Description => "为 Emby 服务器的相关 HTTP 请求配置代理。";

        public override Guid Id => PluginId;

        public sealed override string Name => "Emby Proxy";

        public PluginOptions GetPluginOptions() => GetOptions();

        protected override bool OnOptionsSaving(PluginOptions options)
        {
            options.ProxyUrl = options.ProxyUrl?.Trim().TrimEnd('/');
            return base.OnOptionsSaving(options);
        }

        protected override void OnOptionsSaved(PluginOptions options)
        {
            if (!options.Enabled)
            {
                Logger.Info("EmbyProxy: disabled.");
            }
            else if (ProxySettings.TryParse(options.ProxyUrl, out _, out _, out _))
            {
                var result = CheckHealth(options.ProxyUrl);
                Logger.Info("EmbyProxy: enabled with {0}; health check: {1}.",
                    ProxySettings.GetSafeDescription(options.ProxyUrl),
                    result);
            }

            base.OnOptionsSaved(options);
        }

        protected override void OnCreatePageInfo(PluginPageInfo pageInfo)
        {
            pageInfo.Name = Name;
            pageInfo.DisplayName = "代理服务器";
            pageInfo.EnableInMainMenu = true;
            pageInfo.MenuIcon = "cloud";
            base.OnCreatePageInfo(pageInfo);
        }

        public void Dispose()
        {
            _proxyPatch.Dispose();
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private string CheckHealth(string proxyUrl)
        {
            var result = ProxyHealthChecker.Check(proxyUrl);
            return result.IsAvailable
                ? $"available ({result.LatencyMilliseconds:0} ms)"
                : "unavailable";
        }
    }
}
