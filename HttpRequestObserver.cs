using HarmonyLib;
using MediaBrowser.Model.Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;

namespace EmbyProxy
{
    /// <summary>
    /// Records outbound HTTP requests that do <b>not</b> go through the plugin's proxy.
    /// Requests through the proxy are recorded by <see cref="LoggingWebProxy"/>.
    /// </summary>
    internal sealed class HttpRequestObserver : IDisposable
    {
        private const string HarmonyId = "com.embyproxy.plugin.requests";

        private readonly ILogger _logger;
        private readonly Harmony _harmony;
        private MethodInfo _targetMethod;
        private bool _isPatched;

        public HttpRequestObserver(ILogger logger)
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
                _targetMethod = FindTargetMethod();
                if (_targetMethod == null)
                {
                    const string message = "未找到 HTTP 发送入口，无法记录直连请求";
                    _logger.Warn("EmbyProxy: HTTP send entry point was not found; direct requests are not logged.");
                    RequestLog.SetRequestHookStatus(message);
                    return false;
                }

                var prefix = new HarmonyMethod(
                    typeof(HttpRequestObserver).GetMethod(nameof(SendAsyncPrefix),
                        BindingFlags.Static | BindingFlags.NonPublic));

                _harmony.Patch(_targetMethod, prefix: prefix);
                _isPatched = true;
                RequestLog.SetRequestHookStatus(string.Format("已挂钩 {0}.{1}",
                    _targetMethod.DeclaringType?.Name,
                    _targetMethod.Name));
                _logger.Info("EmbyProxy: request logging patched {0}.{1}.",
                    _targetMethod.DeclaringType?.FullName,
                    _targetMethod.Name);
                return true;
            }
            catch (Exception exception)
            {
                RequestLog.SetRequestHookStatus("挂钩失败: " + exception.Message);
                _logger.Warn("EmbyProxy: failed to patch HTTP request logging: {0}", exception.Message);
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
                _logger.Warn("EmbyProxy: failed to remove request logging patch: {0}", exception.Message);
            }
            finally
            {
                _isPatched = false;
            }
        }

        [HarmonyPrefix]
        private static void SendAsyncPrefix(object __instance, HttpRequestMessage __0)
        {
            try
            {
                var plugin = Plugin.Instance;
                var options = plugin?.CurrentOptions;
                if (plugin == null || options == null)
                {
                    return;
                }

                var uri = __0?.RequestUri;
                if (uri == null)
                {
                    return;
                }

                var proxy = GetProxy(__instance);

                // Proxied requests are recorded by LoggingWebProxy.
                if (proxy is LoggingWebProxy)
                {
                    return;
                }

                if (!options.Enabled || !options.EnableRequestLog)
                {
                    return;
                }

                if (proxy == null && IsInternalTarget(uri))
                {
                    return;
                }

                RequestLog.Record(uri, proxy == null ? RequestKind.Direct : RequestKind.OtherProxy);
            }
            catch
            {
                // Logging must never break an HTTP request.
            }
        }

        private static IWebProxy GetProxy(object handler)
        {
            if (handler is SocketsHttpHandler socketsHttpHandler)
            {
                return socketsHttpHandler.UseProxy ? socketsHttpHandler.Proxy : null;
            }

            if (handler is HttpClientHandler httpClientHandler)
            {
                return httpClientHandler.UseProxy ? httpClientHandler.Proxy : null;
            }

            return null;
        }

        private static bool IsInternalTarget(Uri uri)
        {
            if (uri.IsLoopback ||
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(uri.Host, out var address))
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
            {
                return address.Equals(IPAddress.IPv6Loopback);
            }

            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        private static MethodInfo FindTargetMethod()
        {
            var socketsHttpHandler = typeof(HttpClientHandler).Assembly.GetType("System.Net.Http.SocketsHttpHandler");
            return FindSendAsync(socketsHttpHandler) ?? FindSendAsync(typeof(HttpClientHandler));
        }

        private static MethodInfo FindSendAsync(Type type)
        {
            return type?.GetMethod(
                "SendAsync",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(HttpRequestMessage), typeof(CancellationToken) },
                null);
        }
    }
}
