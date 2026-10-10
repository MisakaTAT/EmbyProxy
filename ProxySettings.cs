using System;
using System.Net;

namespace EmbyProxy
{
    internal static class ProxySettings
    {
        private static readonly string[] BypassAddressList =
        {
            "^https?://10\\.",
            "^https?://172\\.(1[6-9]|2[0-9]|3[0-1])\\.",
            "^https?://192\\.168\\."
        };

        public static bool TryParse(string proxyUrl, out Uri proxyUri, out string username, out string password)
        {
            proxyUri = null;
            username = string.Empty;
            password = string.Empty;

            if (!Uri.TryCreate(proxyUrl?.Trim(), UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(parsed.Host) ||
                parsed.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(parsed.Query) ||
                !string.IsNullOrEmpty(parsed.Fragment) ||
                (!parsed.IsDefaultPort && (parsed.Port < 1 || parsed.Port > 65535)))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(parsed.UserInfo))
            {
                var separator = parsed.UserInfo.IndexOf(':');
                if (separator <= 0 || separator == parsed.UserInfo.Length - 1)
                {
                    return false;
                }

                username = Uri.UnescapeDataString(parsed.UserInfo.Substring(0, separator));
                password = Uri.UnescapeDataString(parsed.UserInfo.Substring(separator + 1));
            }

            proxyUri = new UriBuilder(parsed.Scheme, parsed.Host, parsed.Port).Uri;
            return true;
        }

        public static bool TryCreateWebProxy(string proxyUrl, out IWebProxy proxy)
        {
            proxy = null;

            if (!TryParse(proxyUrl, out var proxyUri, out var username, out var password))
            {
                return false;
            }

            proxy = new WebProxy(proxyUri)
            {
                BypassProxyOnLocal = true,
                BypassList = BypassAddressList,
                Credentials = string.IsNullOrEmpty(username)
                    ? null
                    : new NetworkCredential(username, password)
            };

            return true;
        }

        public static string GetSafeDescription(string proxyUrl)
        {
            if (!TryParse(proxyUrl, out var proxyUri, out _, out _))
            {
                return "无效地址";
            }

            return proxyUri.AbsoluteUri.TrimEnd('/');
        }
    }
}
