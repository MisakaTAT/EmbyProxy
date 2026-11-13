using HarmonyLib;
using MediaBrowser.Model.Logging;
using System;
using System.Linq;
using System.Net;
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

        public bool IsPatched => _isPatched;

        public bool TryApply()
        {
            if (_isPatched)
            {
                return true;
            }

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
                    const string message = "未找到 CreateHttpClientHandler，代理未启用";
                    _logger.Warn("EmbyProxy: CreateHttpClientHandler was not found; proxy is disabled.");
                    RequestLog.SetPatchStatus(message);
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
                RequestLog.SetPatchStatus(string.Format("已挂钩 {0}.{1}",
                    _targetMethod.DeclaringType?.FullName,
                    _targetMethod.Name));
                return true;
            }
            catch (Exception exception)
            {
                _logger.Warn("EmbyProxy: failed to patch Emby HTTP handler: {0}", exception.Message);
                RequestLog.SetPatchStatus("挂钩失败: " + exception.Message);
                RequestLog.NoteError("挂钩失败: " + exception.Message);
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
            if (options == null || !options.Enabled)
            {
                RequestLog.NoteHandler(false);
                return;
            }

            var proxy = plugin.GetCurrentProxy();
            if (proxy == null)
            {
                RequestLog.NoteHandler(false);
                RequestLog.NoteError("代理地址无效，未能配置 HTTP 处理器");
                return;
            }

            try
            {
                if (ConfigureHandler(__result, proxy))
                {
                    RequestLog.NoteHandler(true);
                }
                else
                {
                    RequestLog.NoteHandler(false);
                    RequestLog.NoteError("处理器不支持 Proxy/UseProxy 属性: " + __result.GetType().FullName);
                }
            }
            catch (Exception exception)
            {
                RequestLog.NoteHandler(false);
                RequestLog.NoteError("设置代理失败: " + exception.Message);
                plugin.Logger.Warn("EmbyProxy: failed to configure HTTP proxy: {0}", exception.Message);
            }
        }

        internal static bool ConfigureHandler(object handler, IWebProxy proxy)
        {
            if (handler is HttpClientHandler httpClientHandler)
            {
                httpClientHandler.Proxy = proxy;
                httpClientHandler.UseProxy = true;
                return true;
            }

            var handlerType = handler.GetType();
            var proxyProperty = handlerType.GetProperty("Proxy", BindingFlags.Instance | BindingFlags.Public);
            var useProxyProperty = handlerType.GetProperty("UseProxy", BindingFlags.Instance | BindingFlags.Public);

            if (proxyProperty?.CanWrite != true || useProxyProperty?.CanWrite != true)
            {
                return false;
            }

            proxyProperty.SetValue(handler, proxy, null);
            useProxyProperty.SetValue(handler, true, null);
            return true;
        }
    }
}
