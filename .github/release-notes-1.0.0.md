## ClashProxy 1.0.0

把 mihomo 内核包进一个 WinUI 3 界面：**默认全局直连，只有你点名的网站或程序才走代理。**

### 下载

| 文件 | 适合 | 说明 |
| --- | --- | --- |
| `ClashProxy-1.0.0-setup-x64.exe` | 想要开始菜单图标、能一键卸载 | Inno Setup 安装包，装到 `C:\Program Files\ClashProxy` |
| `ClashProxy-1.0.0-portable-win-x64.zip` | 想解压即用、不写系统 | 绿色便携版，解压到任意目录双击 `ClashProxy.exe` |

`SHA256SUMS.txt` 是两个产物的校验和，下载后可自行核对。

### 系统要求

- Windows 10 1809 及以上（64 位）
- [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Windows App Runtime 1.8](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)（WinUI 3 运行库）

缺后两项的话程序起不来，安装器会检测第一项并给出提示。

### 两条通道

| 通道 | 端口 | 行为 |
| --- | --- | --- |
| 规则通道 | `127.0.0.1:7890` | 只有「分流规则」里列出的域名走代理，其它一律直连（`MATCH,DIRECT`） |
| 应用通道 | `127.0.0.1:7891` | 进了这个端口的流量**全部**走代理，给「应用通道」里启动的进程用 |

系统代理设置不动、环境变量不动，关掉程序内核就停。

### 功能

- 导入 Clash 订阅（支持 `https`/`http` 链接），解析节点列表并逐个测延迟
- 选中节点后启动内核自动生效（走控制接口 `/proxies/PROXY`）
- 分流规则：域名 / 域名后缀 / 关键字三种匹配方式，内置 32 条常用站点预设
- 应用通道：选一个 exe，点「走代理启动」——只给这个进程注入 `HTTP_PROXY/HTTPS_PROXY/ALL_PROXY`，其它程序完全不受影响
- 运行日志、内置使用说明（含开发者信息与「学习项目」声明）
- 内核用 Windows Job Object 绑定：主程序被强杀时内核跟着退出，下次启动还会清扫残留

### 已知限制

- 不认环境变量的程序（例如部分 Electron/Java 应用）需要它自带 `--proxy-server` 之类参数
- 不支持 UDP 转发，没做 TUN 模式；纯按域名/进程转发 TCP 流量
- 订阅里的 `proxy-providers`、`rule-providers` 不保留，只取 `proxies` 节点
- 只支持 Windows x64

### 许可

本项目以 MIT 协议开源；内核为 [MetaCubeX/mihomo](https://github.com/MetaCubeX/mihomo)（GPL-3.0），随包分发。

仅供学习交流使用，不提供任何订阅或节点，请自备订阅并遵守当地法律法规。