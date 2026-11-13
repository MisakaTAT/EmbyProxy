using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using System;
using System.Net;

namespace EmbyProxy
{
    public sealed class Plugin : BasePluginSimpleUI<PluginOptions>, IDisposable
    {
        private static readonly Guid PluginId = new Guid("6a0fa5b3-07fb-44ea-a3e6-e5e47a0c7df9");

        private readonly ProxyPatch _proxyPatch;
        private readonly HttpRequestObserver _requestObserver;
        private readonly object _optionsLock = new object();
        private readonly object _proxyLock = new object();
        private volatile PluginOptions _currentOptions;
        private IWebProxy _cachedProxy;
        private string _cachedProxyUrl;

        public static Plugin Instance { get; private set; }

        public ILogger Logger { get; }

        public PluginOptions CurrentOptions
        {
            get
            {
                var options = _currentOptions;
                if (options != null)
                {
                    return options;
                }

                lock (_optionsLock)
                {
                    options = _currentOptions ?? GetOptions();
                    _currentOptions = options;
                    return options;
                }
            }
        }

        public Plugin(IApplicationHost applicationHost, ILogManager logManager)
            : base(applicationHost)
        {
            Instance = this;
            Logger = logManager.GetLogger(Name);

            IApplicationPaths applicationPaths = null;
            try
            {
                applicationPaths = applicationHost.Resolve<IApplicationPaths>();
            }
            catch (Exception exception)
            {
                Logger.Warn("EmbyProxy: failed to resolve application paths: {0}", exception.Message);
            }

            HarmonyHelperPath.Configure(Logger, applicationPaths);

            try
            {
                _currentOptions = GetOptions();
            }
            catch (Exception exception)
            {
                Logger.Warn("EmbyProxy: failed to load options: {0}", exception.Message);
            }

            UpdateProxyStatus(_currentOptions);

            _proxyPatch = new ProxyPatch(Logger);
            _proxyPatch.TryApply();
            _requestObserver = new HttpRequestObserver(Logger);
            _requestObserver.TryApply();
        }

        public override string Description => "为 Emby 服务器的相关 HTTP 请求配置代理。";

        public override Guid Id => PluginId;

        public sealed override string Name => "Emby Proxy";

        public PluginOptions GetPluginOptions() => CurrentOptions;

        public IWebProxy GetCurrentProxy()
        {
            var options = CurrentOptions;
            if (options == null || !options.Enabled || string.IsNullOrWhiteSpace(options.ProxyUrl))
            {
                return null;
            }

            lock (_proxyLock)
            {
                if (_cachedProxy != null && string.Equals(_cachedProxyUrl, options.ProxyUrl, StringComparison.Ordinal))
                {
                    return _cachedProxy;
                }

                if (!ProxySettings.TryCreateWebProxy(options.ProxyUrl, out var proxy))
                {
                    return null;
                }

                _cachedProxy = new LoggingWebProxy(proxy, () => CurrentOptions?.EnableRequestLog == true);
                _cachedProxyUrl = options.ProxyUrl;
                return _cachedProxy;
            }
        }

        protected override bool OnOptionsSaving(PluginOptions options)
        {
            options.ProxyUrl = options.ProxyUrl?.Trim().TrimEnd('/');
            options.RequestLogView = string.Empty;
            return base.OnOptionsSaving(options);
        }

        protected override void OnOptionsSaved(PluginOptions options)
        {
            _currentOptions = options;

            UpdateProxyStatus(options);

            if (!options.Enabled)
            {
                Logger.Info("EmbyProxy: disabled.");
            }
            else if (ProxySettings.TryParse(options.ProxyUrl, out _, out _, out _))
            {
                var result = CheckHealth(options.ProxyUrl);
                RequestLog.Note(string.Format("健康检查: {0}", result));
                Logger.Info("EmbyProxy: enabled with {0}; health check: {1}.",
                    ProxySettings.GetSafeDescription(options.ProxyUrl),
                    result);
            }

            if (!_proxyPatch.IsPatched)
            {
                _proxyPatch.TryApply();
            }

            if (!_requestObserver.IsPatched)
            {
                _requestObserver.TryApply();
            }

            base.OnOptionsSaved(options);
        }

        protected override PluginOptions OnBeforeShowUI(PluginOptions options)
        {
            var result = base.OnBeforeShowUI(options) ?? options;

            if (result != null)
            {
                result.RequestLogView = RequestLog.Format();
            }

            return result;
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
            RequestLog.SetPatchStatus("插件已卸载");
            _requestObserver.Dispose();
            _proxyPatch.Dispose();
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private static void UpdateProxyStatus(PluginOptions options)
        {
            if (options == null)
            {
                RequestLog.SetProxyStatus("配置未加载");
                return;
            }

            if (!options.Enabled)
            {
                RequestLog.SetProxyStatus("未启用");
                return;
            }

            RequestLog.SetProxyStatus(ProxySettings.TryParse(options.ProxyUrl, out _, out _, out _)
                ? "已启用 → " + ProxySettings.GetSafeDescription(options.ProxyUrl)
                : "已启用，但代理地址无效");
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
