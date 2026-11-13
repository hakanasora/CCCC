# 搞怪付款样式

本次基于原仓库 main 的 `8bc83d1b3031928060fa77c7e99a202d43e3b313` 修改。使用原有 C# 5 / WPF / .NET Framework 4.8 构建流程。

## 使用

1. 完整构建后运行 `artifacts/build/CCCC.exe`。
2. 托盘右键 → 提示样式 → 搞怪付款。普通卡片构建也有此选项。默认样式不变，主动选择才启用。
3. 在其他应用正常复制。仅现有 `FeedbackKind.Confirmed` 进入付款窗口；未确认、保留旧内容、更新和状态信息仍由原提示显示。
4. 选择 WeChat、PayPal 或支付宝只改变窗口内的选择。实际复制已完成，可照常粘贴。二维码和网页入口始终指向 `https://github.com/hakanasora/CCCC`，不实际收费。
5. “无法扫码？点击这里”先展示帮助，再点击“前往收银台”才通过默认浏览器打开仓库。失败时保留帮助并显示目标 URL，不改写剪贴板。
6. “我要白嫖”→“确定”关闭父子窗口；“不白嫖了”或 Esc 只关确认框。Alt+F4 从主窗口进入同一确认流程。右上角 X 只显示两秒短提示。
7. 再次点击托盘已选中的“搞怪付款”恢复此前样式；也可直接选择普通卡片或任务栏单行。暂停、切换样式、退出会直接清理付款窗口和确认框。

同一会话连续复制不重置付款方式、不叠窗、不打断确认框。主窗口首次出现不抢焦点，用户点击后可交互。窗口不会自动两秒关闭。

## 配置兼容与代码入口

`Preferences.UsePrank` 对应 `prank=on/off`，缺少字段时为 false。启用时覆盖成功反馈样式，原有 `layout`、预览和材质偏好保留；关闭后恢复原卡片/任务栏选择。原有稳定版和实验版配置文件名、默认值保持。原 Toast 材质不变；按用户后续授权，复制提示与新增互动窗口默认使用本机 Microsoft YaHei UI / Microsoft YaHei / Segoe UI。

- `App.cs`：托盘入口、反馈分发、暂停/退出和 generation 失效保护。
- `PrankPaymentController.cs`：唯一窗口引用与会话复用。
- `PrankPaymentWindow.cs`：真实 WPF 主窗口、帮助面板、装饰提示和非阻塞的 Owner 确认框。
- `PrankPlacement.cs`：显示器选择、物理像素定位、DIP 尺寸换算、DPI/工作区变化订阅。
- `PrankQrCode.cs` / `PrankBrandMarks.cs`：内置二维码模块和品牌矢量图。
- `assets/prank/README.md`：二维码复现方法、图形来源与许可。

定位优先复制来源前台窗口，其次鼠标所在显示器，最后主显示器。外层按工作区宽 58%、高 63% 定位，面板等比缩小以保持内容完整；极窄或极矮工作区会留透明边距。工作区坐标直接以物理像素传给 SetWindowPos，仅尺寸换算为 DIP，避免对负坐标和不同 DPI 显示器错误缩放。

## 构建

标准构建已改为引用本机系统字体。已实际确认本机安装了 Microsoft YaHei UI、Microsoft YaHei、Segoe UI 和 Bahnschrift，不需要原 vivo 字体，也不复制、安装或嵌入系统字体。

```powershell
.\build.ps1 -CheckQuality -CheckTaskbar -CheckPrank
# 不含任务栏的普通构建：
.\build.ps1 -TaskbarExperiment:$false -CheckQuality -CheckPrank
# 原动画检查保持原先的独立组合：
.\build.ps1 -TaskbarExperiment:$false -CheckAnimation
```

完整程序输出为 `artifacts/build/CCCC.exe`。仍保留显式 `-FontDirectory` 的原 vivo 嵌入构建选项；该可选模式需要对应原字体，不会自动下载。

也可以单独体验付款窗口：

```powershell
.\tests\build-prank.ps1 -CompileApplication -Ui
.\artifacts\prank-checks\CCCC.PrankPreview.exe
```

`CCCC.PrankPreview.exe` 是真实 WPF 界面预览，可点击体验完整付款交互；它没有复制监听、托盘、自启动或用户配置读写。两个 `CompileOnly` exe 仅用于核对生产源码编译，不是发布程序，不应用于正式运行。`-Ui` 检查短暂显示自有测试窗口；省略它只运行状态、配置、几何和离屏布局检查。检查使用随机临时配置，不写真实用户设置。

## 实际验证与限制（2026-11-13）

- 初版 `tests/build-prank.ps1 -CompileApplication -Ui`：新增状态、配置、窗口交互、焦点与受限尺寸检查通过，普通配置 82 项、任务栏配置 85 项；原质量检查各 44 项、任务栏离屏检查 128 项通过。历史日志保存到 `artifacts/prank-checks/validation.log`。
- `tools/generate-prank-qr.py`：PNG 解码精确等于仓库 URL。WPF 的 100%、125%、150%、200% 二维码渲染及完整布局渲染也用 zxing-cpp 核对。四模块白色静区保留。
- 经用户授权改用系统字体后，`build.ps1 -CheckQuality -CheckTaskbar -CheckPrank` 已实际生成完整 `CCCC.exe`；质量检查 44 项、任务栏离屏检查 128 项、新增非交互检查普通配置 57 项／任务栏配置 60 项均通过。原先缺少 vivo 字体造成的标准构建阻塞已经解除。
- `build.ps1 -TaskbarExperiment:$false -CheckQuality -CheckAnimation -OutputDirectory .\artifacts\card-build`：普通卡片完整构建通过、质量检查 44 项、原动画检查 52 项通过；日志在 `artifacts/system-font-card-validation.log`。应用 `--render-preview` 离屏导出的普通卡片中文已查看，显示正常。默认完整 exe 的内置资源确认仅含二维码矩阵，没有系统字体资源包。
- `git diff --check`：用于检查补丁空白错误。
- `artifacts/prank-checks/layout-preview-*.png` 是 WPF 离屏渲染预览，**不是桌面运行截图**。电脑控制工具未获准访问独立预览程序，未取得真实运行截图。
- 未验证：真实多屏混合 DPI 切换、全部物理分辨率与系统缩放组合、手机真机扫码、实际默认浏览器启动、完整 CCCC 的剪贴板复制/粘贴与托盘联动，以及可选原 vivo 字体构建。文字、文件、图片的复制确认仍沿用原 Core/Native 实现，未修改支持范围。

Windows 手动验收时需补充：在普通与任务栏配置上选中搞怪付款，分别复制文字、文件、图片并粘贴；连续复制及确认框可见时复制；帮助返回、浏览器失败与成功、X 连点、Esc/Alt+F4、确认/取消；暂停/切换/退出后不遗留窗口且旧回调不重开；100%/125%/150%/200%、负坐标和不同 DPI 多屏下居中。托盘退出应立即结束，不经过白嫖确认。
