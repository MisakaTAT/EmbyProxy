# EmbyProxy

独立的 Emby 服务器代理插件，基于 Emby `4.10.1.0` 的插件 API 构建。

## 功能

- 配置 HTTP 或 HTTPS 代理
- 支持代理用户名和密码
- 自动绕过本机、`10.0.0.0/8`、`172.16.0.0/12` 和 `192.168.0.0/16`
- 启动时检查 Emby 内部 HTTP 处理器入口
- 保存配置时记录代理连通性检查结果
- 插件页面显示最近的请求日志和代理状态，便于排查代理是否生效
- 代理密码不会写入日志

## 请求日志

插件页面（仪表板 → 插件 → Emby Proxy → 设置）的下方有一个“请求日志”文本框，显示：

- **补丁状态**：是否成功挂钩 Emby 的 `CreateHttpClientHandler`。
- **请求日志挂钩**：是否成功挂钩底层 HTTP 发送入口（用于记录没有走代理的请求）。
- **HTTP 处理器**：Emby 创建处理器的次数，以及成功设置代理的次数。
- **请求**：总计次数，以及其中**代理**、**绕过**、**直连**、**其他代理**各有多少。
- **最近请求**：时间、类别标记、目标地址（已去掉查询参数，避免泄露密钥）。

类别含义：

- `[代理]`：请求使用了本插件设置的代理。
- `[绕过]`：请求命中了本机或内网绕过规则，按直连处理。
- `[直连]`：请求没有使用本插件的代理（例如客户端在插件加载前就已创建）。
- `[其他代理]`：请求使用了非本插件设置的代理。

日志只保存在内存中，最多 200 条，每秒最多记录 20 条，重启 Emby 后清空。重新打开此页面即可刷新；请求量太大时可以关闭“记录请求日志”。

日志里出现 `[直连]`，说明该请求使用的处理器没有本插件的代理，通常是因为处理器在插件加载前就已创建，或由其他组件自行创建。这类处理器在开始工作后无法再修改代理设置，只能等 Emby 重新创建处理器；如果每次启动都在插件加载前创建，重启也无法覆盖。

排查“代理没生效”时按顺序看：

1. 补丁状态显示“未找到 CreateHttpClientHandler” → 当前 Emby 版本没有这个入口，代理不会生效。
2. HTTP 处理器创建次数为 0 且请求全是 `[直连]` → Emby 复用了启动时创建的处理器，当前插件无法覆盖这些处理器，请把日志发给作者确认对应版本。
3. 请求标记为 `[绕过]` → 命中了本机或内网绕过规则。
4. 有 `[代理]` 记录但访问失败 → 代理服务器本身不可用或认证失败，先看保存设置时的健康检查结果。

插件按 Emby `4.10.1.0` 的插件 API 构建。v1.0 曾在 Emby `4.10.1.0` Linux x64 上验证；v1.2.2 的请求日志、请求挂钩和 Harmony 辅助库路径尚未在真实 Emby 实例上验证，如果遇到问题，请反馈版本号、系统架构、插件日志和页面里的请求日志。

## 构建

需要安装 .NET SDK。项目使用 `net6.0`，构建依赖 Emby 插件 API 包。

```bash
dotnet restore
dotnet build -c Release
```

将生成的 `bin/Release/net6.0/EmbyProxy.dll` 复制到 Emby 的 `programdata/plugins/` 目录，然后重启 Emby。Release 构建会自动把 Harmony 合并进主 DLL，安装目录只需要这个 DLL。

## 群晖 DSM 7.2+

群晖内置套件的插件目录是：

```text
/var/packages/EmbyServer/var/plugins/
```

对应的数据目录通常是：

```text
/volume1/@appdata/EmbyServer/plugins/
```

将 DLL 直接放入该目录：

```bash
wget -O /var/packages/EmbyServer/var/plugins/EmbyProxy.dll http://你的下载地址/EmbyProxy.dll
chown emby:emby /var/packages/EmbyServer/var/plugins/EmbyProxy.dll
chmod 644 /var/packages/EmbyServer/var/plugins/EmbyProxy.dll
```

群晖 Emby 套件的用户和组通常是 `emby`（小写）。如果提示 `invalid user` 或 `invalid group`，先用 `id emby` 查看实际的用户和组名，再替换成对应的名称。群晖套件可能因权限导致插件加载失败，复制 DLL 后请确认它与插件目录使用相同的属主和权限。不要放入 `/volume1/@appstore/EmbyServer/system/plugins/`，该目录属于系统自带插件位置。

### 群晖 /tmp 以 noexec 挂载

Harmony（MonoMod）默认把本地辅助库 `mm-exhelper.so` 写到 `/tmp` 并 `dlopen`。群晖等系统把 `/tmp` 以 `noexec` 挂载时，会看到：

```text
Unable to load shared library '/tmp/mm-exhelper.so.XXXXXX'
/tmp/mm-exhelper.so.XXXXXX: failed to map segment from shared object
```

v1.2.2 起插件会在启动时依次尝试 Emby 的 `TempDirectory`、`PluginsPath`、`ProgramDataPath` 和插件 DLL 同级目录，把 MonoMod 的辅助库放到第一个位于可执行挂载点且可写的目录（通常是 `.../plugins/.harmony/`），并在插件页面显示实际使用的目录。

如果仍然报同样的错误，可以临时把 `/tmp` 改成可执行并重启 Emby：

```bash
sudo mount -o remount,exec /tmp
sudo synopkg restart EmbyServer
```

重启后 `/tmp` 会恢复 noexec，可在 DSM 的“任务计划 → 触发的任务 → 开机”里用 root 执行同样的 `mount` 命令。

首次加载时插件会检查 `Emby.Server.Implementations.ApplicationHost` 是否仍有返回 `HttpMessageHandler` 的 `CreateHttpClientHandler` 方法。若找不到，插件会记录警告并保持代理关闭，不会阻止 Emby 启动。

## 限制

插件只修改 Emby 通过该 HTTP 处理器入口创建的客户端。FFmpeg、第三方插件或其他自行创建网络客户端的组件默认不会使用此代理，日志中会显示为 `[直连]`。
