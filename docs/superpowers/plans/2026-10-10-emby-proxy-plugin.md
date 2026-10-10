# Emby 代理插件实现计划

> **面向 AI 代理的工作者：** 本计划已按目标完成实现。

**目标：** 构建一个面向 Emby 4.10.1.0 Linux x64 的独立 HTTP 代理 DLL 插件。

**架构：** 插件入口只负责配置生命周期和 Emby 页面注册。代理 URL 解析、连通性检查和 HTTP 处理器补丁分别放在独立文件中。Harmony 补丁启动时安装一次，每次 Emby 创建 HTTP 处理器时读取最新配置，因此配置修改对新连接立即生效。

**技术栈：** C#、`net6.0`、Emby `MediaBrowser.Server.Core` 插件 API、Harmony 2.3.6。

## 文件结构

- `EmbyProxy.csproj`：目标框架和 Emby/Harmony 依赖。
- `Plugin.cs`：插件入口、配置保存、页面信息和补丁生命周期。
- `PluginOptions.cs`：代理开关、地址和 UI 校验。
- `ProxySettings.cs`：代理 URL 解析、凭据和绕过地址。
- `ProxyHealthChecker.cs`：通过 204 响应检查代理连通性。
- `ProxyPatch.cs`：定位并补丁 Emby 的 HTTP 处理器创建方法。
- `README.md`：构建、安装和限制说明。

## 验证

完成后应使用 .NET SDK 执行 `dotnet restore` 和 `dotnet build -c Release`，并在 Emby 4.10.1.0 Linux x64 实例中检查插件日志及代理请求。
