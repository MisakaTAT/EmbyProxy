using HarmonyLib;
using MediaBrowser.Model.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;

namespace EmbyProxy
{
    internal sealed class ProxyPatch : IDisposable
    {
        private const string HarmonyId = "com.embyproxy.plugin";

        private readonly ILogger _logger;
        private readonly Harmony _harmony;
        private MethodInfo _targetMethod;
        private bool _isPatched;

        public ProxyPatch(ILogger logger)
        {
            _logger = logger;
            _harmony = new Harmony(HarmonyId);
        }

        public bool TryApply()
        {
            try
            {
                var assembly = Assembly.Load("Emby.Server.Implementations");
                var applicationHost = assembly.GetType("Emby.Server.Implementations.ApplicationHost");
                _targetMethod = applicationHost?.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(method => method.Name == "CreateHttpClientHandler")
                    .Where(method => typeof(HttpMessageHandler).IsAssignableFrom(method.ReturnType))
                    .OrderBy(method => method.GetParameters().Length)
                    .ThenBy(method => method.ReturnType == typeof(HttpMessageHandler) ? 0 : 1)
                    .FirstOrDefault();

                if (_targetMethod == null)
                {
                    _logger.Warn("EmbyProxy: CreateHttpClientHandler was not found; proxy is disabled.");
                    return false;
                }

                var postfix = new HarmonyMethod(
                    typeof(ProxyPatch).GetMethod(nameof(CreateHttpClientHandlerPostfix),
                        BindingFlags.Static | BindingFlags.NonPublic));

                _harmony.Patch(_targetMethod, postfix: postfix);
                _isPatched = true;
                _logger.Info("EmbyProxy: patched {0}.{1} returning {2}.",
                    _targetMethod.DeclaringType?.FullName,
                    _targetMethod.Name,
                    _targetMethod.ReturnType.FullName);
                return true;
            }
            catch (Exception exception)
            {
                _logger.Warn("EmbyProxy: failed to patch Emby HTTP handler: {0}", exception.Message);
                return false;
            }
        }

        public void Dispose()
        {
            if (!_isPatched || _targetMethod == null)
            {
                return;
            }

            try
            {
                _harmony.Unpatch(_targetMethod, HarmonyPatchType.All, HarmonyId);
            }
            catch (Exception exception)
            {
                _logger.Warn("EmbyProxy: failed to remove patch: {0}", exception.Message);
            }
            finally
            {
                _isPatched = false;
            }
        }

        [HarmonyPostfix]
        private static void CreateHttpClientHandlerPostfix(object __result)
        {
            var plugin = Plugin.Instance;
            if (plugin == null || __result == null)
            {
                return;
            }

            var options = plugin.GetPluginOptions();
            if (!options.Enabled || !ProxySettings.TryCreateWebProxy(options.ProxyUrl, out var proxy))
            {
                return;
            }

            try
            {
                if (__result is HttpClientHandler httpClientHandler)
                {
                    httpClientHandler.Proxy = proxy;
                    httpClientHandler.UseProxy = true;
                }
                else
                {
                    var handlerType = __result.GetType();
                    var proxyProperty = handlerType.GetProperty("Proxy", BindingFlags.Instance | BindingFlags.Public);
                    var useProxyProperty = handlerType.GetProperty("UseProxy", BindingFlags.Instance | BindingFlags.Public);

                    if (proxyProperty?.CanWrite == true && useProxyProperty?.CanWrite == true)
                    {
                        proxyProperty.SetValue(__result, proxy, null);
                        useProxyProperty.SetValue(__result, true, null);
                    }
                }
            }
            catch (Exception exception)
            {
                plugin.Logger.Warn("EmbyProxy: failed to configure HTTP proxy: {0}", exception.Message);
            }
        }
    }
}
