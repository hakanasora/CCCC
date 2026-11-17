# 开发与构建

## 准备环境

- Windows x64、.NET Framework 4.8，使用 Windows PowerShell 5.1 或 PowerShell 7。
- 当前脚本使用系统的 .NET Framework C# 编译器、WPF 组件、Windows WinMetadata 及 WinRT 引用；缺少组件时会明确报错。
- 不需要 Visual Studio、NuGet、额外运行时或联网构建。源码使用 C# 5。
- 自行获取 [vivo Sans 的两个原始字体文件](../assets/fonts/README.md)，放入 `assets/fonts/`。它们不会被 Git 跟踪。

## 构建

在仓库根目录执行：

```powershell
.\build.ps1
```

生成 `artifacts/build/CCCC.exe`。当前源码版本为 **2.5.0**，包含普通卡片与实验性任务栏样式。Git 忽略默认输出目录。

也可以指定本地字体目录和输出目录：

```powershell
.\build.ps1 -FontDirectory 'D:\MyFonts\vivoSans' -OutputDirectory '.\artifacts\custom'
```

字体目录内必须有 `vivoSans-Medium.ttf` 和 `vivoSans-Bold.ttf`。脚本不会下载、改写、裁剪或安装字体，也不会静默切换成其他字体。

## 可选轻量检查

配置、复制状态机和资源生命周期检查（不修改真实用户设置）：

```powershell
.\build.ps1 -CheckQuality
```

代码质量优化及验证范围见 [2026-10-28 记录](quality-2026-10-28.md)。

任务栏布局与样式切换的离屏检查：

```powershell
.\build.ps1 -CheckTaskbar
```

这项检查不启动复制监听、托盘、UI Automation 探测或桌面弹窗。普通构建不自动运行检查。

原有动画诊断需使用只包含普通卡片的兼容构建：

```powershell
.\build.ps1 -TaskbarExperiment:$false -CheckAnimation
```

此命令构建普通卡片程序和独立动画诊断程序，执行当前的淡入淡出、首次／重复复制和取消待执行请求检查。检查使用透明的自有窗口，不启动剪贴板监听或托盘，不写真实用户配置、自启动，也不制作截图。检查数据保存在输出目录。

诊断源码和故障注入不进入交付 exe。历史回归及内存实验继续留在迁移前的归档中。

## 目录与后续工作

- `src/CCCC/`：2.5.0 源码，包含普通卡片和实验性任务栏样式。
- `assets/`：图标、清单与本地字体说明。
- `tests/`：动画及任务栏离屏轻量检查。
- `docs/`：产品素材、使用方法、设计参数及验证记录。
- `artifacts/`：被忽略的构建结果。

后续直接在此仓库修改，用小而清晰的 Git 提交记录改动，用版本标签标记里程碑；不再复制整套版本目录。每轮只运行与改动相关的轻量检查。

GitHub 自动生成的标签源码归档需要按上文自行准备字体。2.5.0 继续读取预览版的设置文件。该文件缺失时，程序读取早期普通卡片版的内容预览和材质设置。开机自启动由程序首次运行或托盘菜单设置，构建和检查使用隔离数据。
