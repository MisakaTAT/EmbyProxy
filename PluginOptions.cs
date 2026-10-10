using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
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

        protected override void Validate(ValidationContext context)
        {
            if (Enabled && !ProxySettings.TryParse(ProxyUrl, out _, out _, out _))
            {
                context.AddValidationError("代理地址无效。格式为 http[s]://[用户名:密码@]主机:端口。");
            }
        }
    }
}
