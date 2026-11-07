# ClashProxy v1.0.1

修复两个上手就会踩到的坑，并新增一个让浏览器直接能用的开关。

## 新增

- **「系统代理（给浏览器用）」开关**（总览页）
  打开后把 Windows 系统代理指向 `127.0.0.1:7890`，浏览器、以及其它读取系统代理的程序不用加任何参数，就能按分流规则访问列表里的网站。
  关闭开关、停止内核、退出程序这三种情况都会把注册表原样还原——不会给你的系统留下改过的代理设置。

## 修复

- **「保存了规则，但生成配置失败」**
  以前每次改规则都会先重新下载一遍订阅，订阅域名一超时，规则就保存不了。
  现在改规则只用本地缓存 `profiles\current.yaml`，断网也能改规则；只有「下载并解析」和「应用到内核」才会联网。

- **失败提示是空的**
  以前只显示 `ex.Message`，异常没有消息时后面就一片空白。现在会带上异常类型（比如 `HttpRequestException`），完整堆栈写进「运行日志」页。

## 升级建议

- 安装版：直接运行新的 `ClashProxy-1.0.1-setup-x64.exe`，会自动覆盖安装，`%LOCALAPPDATA%\ClashProxy` 里的订阅、节点、规则都保留。
- 便携版：解压覆盖原来的目录即可（`settings.json` 在数据目录里，不受影响）。

## 校验

```
SHA256  ClashProxy-1.0.1-setup-x64.exe          bd9d745025c93f09171f8fb99503dc6b238eec852b61beb7ec835a0e8ab73cb2
SHA256  ClashProxy-1.0.1-portable-win-x64.zip   f92cfd915dc04093cc06f0ae2b10d2bf557c0a660ddcbe1c1dfb335b7edc2446
```

---

这是一个学习项目，不提供任何订阅或节点。内核是 [MetaCubeX/mihomo](https://github.com/MetaCubeX/mihomo)（GPL-3.0），随包分发。