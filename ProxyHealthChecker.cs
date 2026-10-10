using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace EmbyProxy
{
    internal static class ProxyHealthChecker
    {
        private static readonly string[] CheckUrls =
        {
            "https://www.gstatic.com/generate_204",
            "https://www.google.com/generate_204"
        };

        public static (bool IsAvailable, double? LatencyMilliseconds) Check(string proxyUrl)
        {
            if (!ProxySettings.TryParse(proxyUrl, out var proxyUri, out var username, out var password))
            {
                return (false, null);
            }

            try
            {
                using (var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy(proxyUri)
                    {
                        Credentials = string.IsNullOrEmpty(username)
                            ? null
                            : new NetworkCredential(username, password)
                    },
                    UseProxy = true
                })
                using (var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(3)
                })
                {
                    var stopwatch = Stopwatch.StartNew();
                    var tasks = new Task<HttpResponseMessage>[CheckUrls.Length];

                    for (var index = 0; index < CheckUrls.Length; index++)
                    {
                        tasks[index] = client.GetAsync(CheckUrls[index]);
                    }

                    var completed = Task.WhenAny(tasks).GetAwaiter().GetResult();
                    stopwatch.Stop();

                    foreach (var task in tasks)
                    {
                        if (task.Status == TaskStatus.RanToCompletion &&
                            task.Result.StatusCode == HttpStatusCode.NoContent)
                        {
                            return (true, stopwatch.Elapsed.TotalMilliseconds);
                        }
                    }
                }
            }
            catch
            {
            }

            return (false, null);
        }
    }
}
