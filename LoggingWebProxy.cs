using System;
using System.Net;

namespace EmbyProxy
{
    /// <summary>
    /// Wraps the real proxy so every proxy lookup can be shown on the plugin page.
    /// </summary>
    internal sealed class LoggingWebProxy : IWebProxy
    {
        private readonly IWebProxy _inner;
        private readonly Func<bool> _isLoggingEnabled;

        public LoggingWebProxy(IWebProxy inner, Func<bool> isLoggingEnabled)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _isLoggingEnabled = isLoggingEnabled;
        }

        public ICredentials Credentials
        {
            get => _inner.Credentials;
            set => _inner.Credentials = value;
        }

        public Uri GetProxy(Uri destination)
        {
            var proxyAddress = _inner.GetProxy(destination);

            if (IsLoggingEnabled())
            {
                var bypassed = _inner.IsBypassed(destination);
                RequestLog.Record(destination, bypassed ? RequestKind.Bypassed : RequestKind.Proxied);
            }

            return proxyAddress;
        }

        public bool IsBypassed(Uri host) => _inner.IsBypassed(host);

        private bool IsLoggingEnabled()
        {
            try
            {
                return _isLoggingEnabled != null && _isLoggingEnabled();
            }
            catch
            {
                return false;
            }
        }
    }
}
