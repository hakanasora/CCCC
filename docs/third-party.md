# 字体与第三方说明

## 项目代码

CCCC 项目代码使用根目录的 [MIT License](../LICENSE)，保留既有版权署名。该许可证不为第三方字体重新授权。

## vivo Sans

卡片标题、预览及部分状态符号使用 vivo Sans 中文品牌版：Medium（真实字重 500）和 Bold（真实字重 650）。原始字体内含版权声明：

> Copyright(c) vivo Mobile Communication Co., Ltd. 维沃移动通信有限公司

- [vivo 官方发布与授权入口](https://developers.vivo.com/doc/d/314fa33cbaec4a93be351cd44757d9d9)
- 字体内的授权信息还指向 [方正字库授权说明](http://www.foundertype.com/index.php/About/bookAuth/key/my_grfs.html)。

字体不属于 CCCC 的 MIT 许可。本仓库不分发原始 TTF 文件，也不把“免费商用”视为字体采用开源许可证的证明。请自行从官方获取文件、阅读并遵守完整协议。

构建时使用原始字体文件，不裁字、不转换、不安装字体；字体嵌入到生成的程序资源中。仅运行已经构建好的程序时，使用者无需另行安装字体。源码编译者需按[本地字体说明](../assets/fonts/README.md)提供文件。

本说明是来源记录，不替代原作者的完整协议。更详细的里程碑字体参数见[字体说明](fonts-v2.4.6.txt)。

## 系统字体与组件

计数器引用系统 Bahnschrift，缺失时回退 Segoe UI；Windows 原生托盘菜单使用系统字体。本仓库不复制或分发这些系统字体文件。WPF、WinForms 和 Windows Composition 使用系统提供的组件。
