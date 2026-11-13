# 搞怪付款资源与来源

## 二维码

唯一内容：`https://github.com/hakanasora/CCCC`。

- `repository-qr.png`：370 × 370，黑白、纠错 M、版本 3，每模块 10 像素，四模块白色静区。
- `repository-qr.txt`：同一二维码的 37 × 37 模块矩阵（包括静区）。以 `CCCC.Prank.RepositoryQr` 嵌入所有应用与诊断构建。窗口按整数物理像素绘制模块，无运行时编码库或网络依赖。
- 生成脚本：`tools/generate-prank-qr.py`，使用 Python 3.12、qrcode 8.2、zxing-cpp 2.3.0 和 Pillow。qrcode 为 BSD-3-Clause，ZXing-C++ 为 Apache-2.0，Pillow 为 HPND；仅用于开发，不随程序运行。

复现（已有开发 Python 环境中执行）：

```powershell
python -m pip install qrcode==8.2 zxing-cpp==2.3.0 Pillow
python .\tools\generate-prank-qr.py
```

脚本重新生成 PNG 和矩阵，再通过独立解码器核对精确 URL。正常构建只消耗已提交的矩阵，不需要 Python、pip 或网络。

## 品牌标志

运行时使用 `src/CCCC/PrankBrandMarks.cs` 中的本地 WPF 矢量路径，不加载网页、不使用字体图标或 emoji。

| 标志 | 原始来源 | 许可及改动 |
| --- | --- | --- |
| WeChat | [Simple Icons wechat.svg](https://github.com/simple-icons/simple-icons/blob/develop/icons/wechat.svg)，blob `c3eb6c4a666f66fcda5df187936c5fee829d4ecb` | [CC0 1.0](https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md)；原路径转换为 WPF Geometry，填充绿色 |
| 支付宝 | [Simple Icons alipay.svg](https://github.com/simple-icons/simple-icons/blob/develop/icons/alipay.svg)，blob `30fa27f229cf513542f730ada52b4b94bfe3a1ca` | CC0 1.0；原路径转换为 WPF Geometry，填充品牌蓝，白底呈现白色“支” |
| PayPal | [Font Awesome Free 7.3.1 paypal.svg](https://github.com/FortAwesome/Font-Awesome/blob/7.x/svgs/brands/paypal.svg)，blob `3528d553095eda5a5223d9e58e22b6a8409e0043` | Copyright 2026 Fonticons, Inc.，[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)，[原项目许可](https://fontawesome.com/license/free)；沿第二个 M 子路径拆为两个 P，分别填充深蓝和浅蓝 |

这些许可覆盖图形文件的使用，不授予相关品牌商标权；本地玩笑界面不代表与品牌有合作关系或提供真实支付服务。
