using System;
using System.Collections.Generic;
using System.Text;

namespace EmbyProxy
{
    internal enum RequestKind
    {
        Proxied,
        Bypassed,
        Direct,
        OtherProxy
    }

    /// <summary>
    /// Holds the recent HTTP requests shown on the plugin page.
    /// Entries only live in memory and are capped, so recording stays cheap.
    /// </summary>
    internal static class RequestLog
    {
        private const int Capacity = 200;
        private const int MaxEntriesPerSecond = 20;

        private static readonly object Gate = new object();
        private static readonly Queue<Entry> Entries = new Queue<Entry>(Capacity);

        private static long _totalRequests;
        private static long _proxiedRequests;
        private static long _bypassedRequests;
        private static long _directRequests;
        private static long _otherProxyRequests;
        private static long _handlersCreated;
        private static long _handlersConfigured;
        private static long _droppedEntries;
        private static long _rateSecond;
        private static int _rateCount;

        private static string _patchStatus = "尚未初始化";
        private static string _requestHookStatus = "尚未初始化";
        private static string _proxyStatus = "未启用";
        private static string _lastError = string.Empty;

        public static void SetPatchStatus(string status)
        {
            lock (Gate)
            {
                _patchStatus = string.IsNullOrWhiteSpace(status) ? "未知" : status;
            }
        }

        public static void SetRequestHookStatus(string status)
        {
            lock (Gate)
            {
                _requestHookStatus = string.IsNullOrWhiteSpace(status) ? "未知" : status;
            }
        }

        public static void SetProxyStatus(string status)
        {
            lock (Gate)
            {
                _proxyStatus = string.IsNullOrWhiteSpace(status) ? "未知" : status;
            }
        }

        public static void NoteHandler(bool configured)
        {
            lock (Gate)
            {
                _handlersCreated++;
                if (configured)
                {
                    _handlersConfigured++;
                }
            }
        }

        public static void NoteError(string message)
        {
            lock (Gate)
            {
                _lastError = message ?? string.Empty;
            }
        }

        public static void Note(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Enqueue(new Entry { Time = DateTime.Now, Category = "信息", Text = message }, false);
        }

        public static void Record(Uri target, RequestKind kind)
        {
            if (target == null)
            {
                return;
            }

            var entry = new Entry
            {
                Time = DateTime.Now,
                Category = GetCategory(kind),
                Text = Describe(target)
            };

            lock (Gate)
            {
                _totalRequests++;
                switch (kind)
                {
                    case RequestKind.Proxied:
                        _proxiedRequests++;
                        break;
                    case RequestKind.Bypassed:
                        _bypassedRequests++;
                        break;
                    case RequestKind.Direct:
                        _directRequests++;
                        break;
                    default:
                        _otherProxyRequests++;
                        break;
                }
            }

            Enqueue(entry, true);
        }

        private static string GetCategory(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.Proxied:
                    return "代理";
                case RequestKind.Bypassed:
                    return "绕过";
                case RequestKind.Direct:
                    return "直连";
                default:
                    return "其他代理";
            }
        }

        public static string Describe(Uri uri)
        {
            if (uri == null)
            {
                return "(未知地址)";
            }

            var path = uri.AbsolutePath;
            if (path.Length > 90)
            {
                path = path.Substring(0, 87) + "...";
            }

            var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
            return string.Format("{0}://{1}{2}{3}", uri.Scheme, uri.Host, port, path);
        }

        public static string Format()
        {
            List<Entry> entries;
            long total;
            long proxied;
            long bypassed;
            long direct;
            long otherProxy;
            long handlersCreated;
            long handlersConfigured;
            long dropped;
            string patchStatus;
            string requestHookStatus;
            string proxyStatus;
            string lastError;

            lock (Gate)
            {
                entries = new List<Entry>(Entries);
                total = _totalRequests;
                proxied = _proxiedRequests;
                bypassed = _bypassedRequests;
                direct = _directRequests;
                otherProxy = _otherProxyRequests;
                handlersCreated = _handlersCreated;
                handlersConfigured = _handlersConfigured;
                dropped = _droppedEntries;
                patchStatus = _patchStatus;
                requestHookStatus = _requestHookStatus;
                proxyStatus = _proxyStatus;
                lastError = _lastError;
            }

            var builder = new StringBuilder();
            builder.AppendLine("插件版本: " + typeof(Plugin).Assembly.GetName().Version);
            builder.AppendLine("代理状态: " + proxyStatus);
            builder.AppendLine("补丁状态: " + patchStatus);
            builder.AppendLine("请求日志挂钩: " + requestHookStatus);
            builder.AppendLine(string.Format("HTTP 处理器: 创建 {0} 个，成功设置代理 {1} 个", handlersCreated, handlersConfigured));
            builder.AppendLine(string.Format("请求: {0} 次（代理 {1}，绕过 {2}，直连 {3}，其他代理 {4}）", total, proxied, bypassed, direct, otherProxy));

            if (dropped > 0)
            {
                builder.AppendLine(string.Format("因频率限制未记录: {0} 条", dropped));
            }

            if (!string.IsNullOrEmpty(lastError))
            {
                builder.AppendLine("最近错误: " + lastError);
            }

            builder.AppendLine();

            if (entries.Count == 0)
            {
                builder.AppendLine("暂无请求记录。");
                builder.AppendLine();
                builder.AppendLine("可能原因：");
                builder.AppendLine("1. 代理尚未启用，或代理地址无效；");
                builder.AppendLine("2. 补丁状态显示为未挂钩，说明没有找到 Emby 的 HTTP 处理器入口；");
                builder.AppendLine("3. 请求日志挂钩失败，或“记录请求日志”已关闭；");
                builder.AppendLine("4. Emby 还没有发起可记录的外部 HTTP 请求（本机和内网直连请求不会记录）。");
                builder.AppendLine();
                builder.AppendLine("可以先刷新元数据或播放网络视频，然后重新打开此页面查看。");
                return builder.ToString();
            }

            builder.AppendLine(string.Format("最近 {0} 条（最新在前，最多保留 {1} 条）:", entries.Count, Capacity));

            for (var index = entries.Count - 1; index >= 0; index--)
            {
                var entry = entries[index];
                builder.AppendLine(string.Format("{0:HH:mm:ss} [{1}] {2}", entry.Time, entry.Category, entry.Text));
            }

            builder.AppendLine();
            builder.AppendLine("提示: 以上记录只保存在内存中，重启 Emby 后清空；关闭“记录请求日志”可停止记录。");
            return builder.ToString();
        }

        private static void Enqueue(Entry entry, bool applyRateLimit)
        {
            var second = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;

            lock (Gate)
            {
                if (applyRateLimit)
                {
                    if (second != _rateSecond)
                    {
                        _rateSecond = second;
                        _rateCount = 0;
                    }

                    if (_rateCount >= MaxEntriesPerSecond)
                    {
                        _droppedEntries++;
                        return;
                    }

                    _rateCount++;
                }

                Entries.Enqueue(entry);

                while (Entries.Count > Capacity)
                {
                    Entries.Dequeue();
                }
            }
        }

        private sealed class Entry
        {
            public DateTime Time { get; set; }

            public string Category { get; set; }

            public string Text { get; set; }
        }
    }
}
