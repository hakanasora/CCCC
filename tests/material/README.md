# 检查普通卡片的材质边缘

本检查会短暂显示专用窗口，并截取卡片区域。截图保存在本地构建目录，不提交到 Git。它不进入程序主入口，不监听或写入剪贴板，不修改用户配置或自启动。

在仓库根目录编译。字体要求与正常构建相同。

```powershell
.\tests\material\build.ps1
$probe = '.\artifacts\material-checks\CCCC.MaterialChecks.exe'
& $probe lifecycle
& $probe color desktop "$PWD\artifacts\material-checks\marker-dark" dark
python .\tests\material\check-rim.py .\artifacts\material-checks\marker-dark
```

需要当前屏幕处于 100% 缩放，且卡片区域没有被其他窗口遮挡。纯桌面是原缺陷的重要触发条件。执行前手动最小化其他窗口，或在一次受控脚本中使用 Shell.Application.MinimizeAll 并在 finally 中 UndoMinimizeALL。检查程序不更改分辨率、刷新率或系统缩放。

捕获模式为 `normal`、`color`、`off`。背景为 `desktop`、`background`、`checker`。第三个参数为输出目录，第四个参数为 `dark` 或 `light`。输出目录应使用新的空目录。每次约三秒。

`color` 将原生模糊画刷替换为洋红色，保留实际背景图、布局和运动路径。分析器检查完整弹跳时是否有大量未被底板覆盖的洋红像素。它拒绝其他模式、非 96 DPI、材质或标记失效、没有充分弹跳活动及缺失截图的记录。卡片内部还必须能看到预期的着色标记，空白截图不能作为通过。120 像素是边缘栅格误差的诊断容差，不是显示器闪烁标准。

`normal` 保留真实毛玻璃。分别运行深浅主题、部分背景窗口和动态棋盘，并查看完整序列。标记检查通过不能替代这些观察，也不能证明所有显卡和刷新率都正常。

`lifecycle` 检查材质关闭、创建失败、背景上传拒绝后的不透明回退，以及重新开启。它使用测试专属的创建失败开关和无效像素格式注入，不操作真实设置文件。

此程序用反射读取内部绘制状态，是诊断工具，不是生产接口。捕获线程的时间戳和请求缩放值不等于屏幕呈现时间。新布局或主题改变时，需要同步调整标记分析器的采样位置与颜色范围。
