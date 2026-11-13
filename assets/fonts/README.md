# 本地字体目录

默认构建使用 Windows 已安装的微软雅黑 UI／微软雅黑／Segoe UI，不需要在此放置字体。这里只跟踪本说明，不提交字体二进制文件。

如需原来的 vivo Sans 外观，可自行从 [vivo 官方发布页](https://developers.vivo.com/doc/d/314fa33cbaec4a93be351cd44757d9d9) 获取字体并遵守原作者协议。将 Brand 中文品牌版的 `vivoSans-Medium.ttf`（真实字重 500）和 `vivoSans-Bold.ttf`（真实字重 650）放在本地目录，构建时显式使用 `-FontDirectory` 指定它（包括放在本目录时也需传入 `-FontDirectory .\assets\fonts`）。

只有显式指定上述参数的构建才内嵌两个原字体，缺少文件会报错。默认构建不内嵌系统字体，也不再要求 vivo 字体。

v2.4.6 采用的原始文件 SHA-256：

```text
66b7d15d289856fd95153035a24543b7ada7abc0cd5e77f33babd5610f5cc53f  vivoSans-Medium.ttf
a99f240e2e6c1a91db81b10b94e54bd79372c8bdb39013dbb9edb3312e856456  vivoSans-Bold.ttf
```
