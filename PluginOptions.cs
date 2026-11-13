using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Model.Attributes;
using System;
using System.ComponentModel;

namespace EmbyProxy
{
    public sealed class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "代理服务器";

        [DisplayName("启用代理")]
        [Description("让 Emby 服务器发起的相关 HTTP 请求使用代理。修改后立即对新建连接生效。")]
        public bool Enabled { get; set; }

        [DisplayName("代理地址")]
        [Description("格式：http://[用户名:密码@]主机:端口；也支持 https 代理。")]
        public string ProxyUrl { get; set; } = string.Empty;

        [DisplayName("记录请求日志")]
        [Description("记录最近的 HTTP 请求及代理状态（最多 200 条），用来确认代理是否生效。请求量很大时可以关闭。")]
        public bool EnableRequestLog { get; set; } = true;

        [DisplayName("请求日志")]
        [Description("只读显示，最多保留 200 条。修改设置并保存、或重新打开此页面即可刷新。")]
        [EditMultiline(14)]
        public string RequestLogView { get; set; } = string.Empty;

        protected override void Validate(ValidationContext context)
        {
            if (Enabled && !ProxySettings.TryParse(ProxyUrl, out _, out _, out _))
            {
                context.AddValidationError("代理地址无效。格式为 http[s]://[用户名:密码@]主机:端口。");
            }
        }
    }
}
