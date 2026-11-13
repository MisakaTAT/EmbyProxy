# EmbyProxy

独立的 Emby 服务器代理插件，基于 Emby `4.10.1.0` 的插件 API 构建。

## 功能

- 配置 HTTP 或 HTTPS 代理
- 支持代理用户名和密码
- 自动绕过本机、`10.0.0.0/8`、`172.16.0.0/12` 和 `192.168.0.0/16`
- 启动时检查 Emby 内部 HTTP 处理器入口
- 保存配置时记录代理连通性检查结果
- 插件页面显示请求日志和代理状态
- 代理密码不会写入日志

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
cd /var/packages/EmbyServer/var/plugins
wget -O EmbyProxy.dll http://你的下载地址/EmbyProxy.dll
sha256sum EmbyProxy.dll
# v1.2.2: d7effedc8513a1b2da2b75dd1b7af2c9113ea2fe66b0277181bbdff6c6383f98
chown emby:emby EmbyProxy.dll
chmod 644 EmbyProxy.dll
sudo synopkg restart EmbyServer
```

群晖 Emby 套件的用户和组通常是 `emby`（小写）。如果提示 `invalid user` 或 `invalid group`，先用 `id emby` 查看实际的用户和组名，再替换成对应的名称。群晖套件可能因权限导致插件加载失败，复制 DLL 后请确认它与插件目录使用相同的属主和权限。不要放入 `/volume1/@appstore/EmbyServer/system/plugins/`，该目录属于系统自带插件位置。

首次加载时插件会检查 `Emby.Server.Implementations.ApplicationHost` 是否仍有返回 `HttpMessageHandler` 的 `CreateHttpClientHandler` 方法。若找不到，插件会记录警告并保持代理关闭，不会阻止 Emby 启动。

## 限制

插件只修改 Emby 通过该 HTTP 处理器入口创建的客户端。FFmpeg、第三方插件或其他自行创建网络客户端的组件默认不会使用此代理，日志中会显示为 `[直连]`。
