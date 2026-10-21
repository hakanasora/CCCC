# 本地字体目录

这里只跟踪本说明，不提交字体二进制文件。请自行从 [vivo 官方发布页](https://developers.vivo.com/doc/d/314fa33cbaec4a93be351cd44757d9d9) 获取字体并遵守原作者协议。

将 Brand 中文品牌版的 `vivoSans-Medium.ttf`（真实字重 500）和 `vivoSans-Bold.ttf`（真实字重 650）放在此目录，或使用构建参数 `-FontDirectory` 指定其他本地目录。

构建后的 exe 会内嵌这两个字体，使用者无需安装。缺少字体时构建会报错，不以其他字体冒充里程碑效果。

v2.4.6 采用的原始文件 SHA-256：

```text
66b7d15d289856fd95153035a24543b7ada7abc0cd5e77f33babd5610f5cc53f  vivoSans-Medium.ttf
a99f240e2e6c1a91db81b10b94e54bd79372c8bdb39013dbb9edb3312e856456  vivoSans-Bold.ttf
```
