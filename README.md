# NEIDFill

身份证读卡填入独立小工具：后台自动读卡，把身份证号以条码枪语义（SendInput 逐字符 Unicode 键事件）
键入**当前前台**输入框。不落库、不上传、不显示人像；全程不抢焦点。

## 来源

本工程从 NEIDScan 项目抽离为**完全独立**的仓库与解决方案（不引用、不依赖 NEIDScan 的任何工程）；
读卡行为模型来自 sfzscan/NECapture 对精伦 iDR240 的实机确证结论（见 `libs\README.md`）。

## 构建与测试

```
dotnet build NEIDFill.sln -c Release
dotnet test NEIDFill.sln
```

输出在 `src\NEIDFill\bin\Release\net48\`。部署时**直接复制整个该目录**
（`NEIDFill.exe`、`NEIDFill.Core.dll`、`NEIDFill.Devices.dll` 与 5 个厂商原生 DLL；
`.pdb` 调试符号可不必复制）。

运行前提：.NET Framework 4.8；x86-only（厂商 Sdtapi.dll 仅 32 位），无其它第三方依赖；
Windows 8.1 / 10 / 11（app.manifest 已声明 supportedOS）。

部署目录**无需可写**：设置（`neidfill.json`）与崩溃日志（`crash.log`）都在
`%LOCALAPPDATA%\NEIDFill\`（详见「设置与日志」），可直接安装到 `Program Files`。
首次启动时若程序目录存在旧版 `neidfill.json`，会自动读取并写入新位置（旧文件保留不删）。

## 版本

`AssemblyVersion`/`ProductVersion` 固定 `1.0.0`；`FileVersion` 为构建时刻 `yyyy.MM.dd.HHmm`
（如 `2026.11.25.1530`），窗口标题显示的就是 FileVersion。

## 使用前提与守卫

- 同一读卡器硬件同一时刻只允许一个程序占用（原生设备独占，互斥锁防本程序双开）；
  第二实例启动时不会重复运行，而是激活已有窗口。**勿与 NEIDScan 同时运行同一台读卡器**。
- 互斥锁是 `Local\`（按登录会话生效）：快速用户切换 / RDP 多会话下每个会话可各跑一个实例；
  请勿让两个会话同时占用同一台读卡器。
- 使用前把光标放进目标输入框并清空原内容；本程序不 Activate、不抢焦点。
- 守卫判定：本程序在前台、前台是桌面（锁屏/登录界面等安全桌面也归为此类）或无前台窗口时
  不注入，并给出提示音与状态说明。
- 同号 2 秒去抖：成功注入后 2 秒内对同一号码重复读卡不再次注入。
- 「追加回车」开关：开则号码后追加回车（默认关）。
- 「自动填入」开关：关则只读卡不注入（默认开）。
- 程序运行即自动读卡；需要把读卡器让给其它程序时退出程序即可（退出会断开并释放读卡器）。
- 系统托盘：最小化时隐藏到托盘（首次有气泡提示），双击托盘图标恢复窗口；右键菜单含
  「显示主窗口 / 退出」；**关闭窗口仍然退出程序**。
- 状态区两行（状态行与结果行各一行，超长按省略号截断、悬停显示全文）：
  第一行连接/读卡状态（连接成功绿、等待/重试琥珀、错误红），
  第二行「上次结果」（已填入/未填入/设置保存失败等，持久显示）。
  状态卡底色区分连接状态：**已连接浅绿、其它（等待/错误）浅灰**；高对比度模式跟随系统色。
  结果行为空时状态文字在卡内垂直居中。

## 读卡行为

- 连接通道自动依次探测 1001/2001/2002，不支持手动指定。
- 无卡时安静等待，不会周期性报错、不计时间；检测到卡在位后才开 30 秒尝试预算。
  轮询间隔：无卡 400ms，卡在位 150ms。
- 空闲时每 60 秒静默重建一次会话（断连重开），用于拔插读卡器后自愈；重建失败会转
  「读卡器未连接，正在重试…」。
- 读卡失败只报错、不断开连接；错误文案分型：认证未就绪 / 读取失败（含返回码）/
  号码校验未通过 / 证件号码为空 / 未识别的卡类型 / 证件类型判定失败。
- 失败后按提示「拿开证件 3 秒后重放」再试（认证只在新鲜放卡后短暂有效）。

## 填入行为

- 逐字符键入，每个字符发送前重新校验前台未变；中途焦点变化或注入被拒会如实提示
  「已键入 n/18 字符，请清空后重试」，此时输入框里已有半截号码，应先清空再重试。
- 整段号码已键入但回车未发出时会单独提示「已填入，回车未发送（请手动回车）」。
- 锁屏/登录界面（安全桌面）归为不可注入目标，不会尝试键入。

## 限制

- 无法判断焦点是否恰好落在“身份证号输入框”——程序只知道前台是其它窗口，光标放错框会填错地方。
- 无法向以管理员权限运行的目标窗口注入（UIPI 拦截 SendInput，会提示失败）。
- 部分只监听 keydown 键码的网页/控件收不到 Unicode 键事件，不会填入。
- 仅 32 位（厂商 Sdtapi.dll 只有 x86 版本）。
- 高 DPI 采用系统级感知（manifest `dpiAware=true`）：主屏缩放下清晰；窗口跨不同 DPI 的显示器时由系统位图缩放。

## 隐私

不把芯片人像读入程序内存；厂商 DLL 写出的临时人像 `%TEMP%\NEIDFill_chip\photo.jpg`
会在每次读卡前、读卡后与断开/关窗时删除，**启动时还会清理上次运行残留的人像**；
不落库、不上传、不留任何证件数据。
界面只显示姓名与操作结果，不显示完整号码；号码仅用于键入前台输入框。

## 设置与日志

- 设置存于 `%LOCALAPPDATA%\NEIDFill\neidfill.json`（自动填入、追加回车），**原子写入**
  （先写临时文件再替换）；文件损坏时静默回退默认值，不阻塞启动；保存失败不再静默丢失，
  会在状态区「上次结果」行提示。
- 首次启动若程序目录存在旧版 `neidfill.json`，会自动读取并写入新位置（旧文件保留不删）；
  `%LOCALAPPDATA%` 不可用时回退到程序目录。
- 未处理异常写入 `%LOCALAPPDATA%\NEIDFill\crash.log`（LocalAppData 不可用时回退程序目录；
  UI 线程异常限流弹窗，进程不无声退出）；日志会把异常文本中的
  18 位身份证号脱敏，单文件超过 1MiB 自动截断。

## 厂商 DLL 授权（重要）

`libs\` 内 5 个厂商 DLL **无厂商再分发授权文件**（详见 `libs\README.md` 与
`THIRD-PARTY-NOTICES.md`）。本仓库包含它们仅用于内部构建与部署；对外发布、随产品分发或开源前，必须先取得厂商授权，或改以获得许可的
制品库方式分发。

仓库整体许可为**专有、仅内部使用**，见 `LICENSE`；变更记录见 `CHANGELOG.md`，安全说明见 `SECURITY.md`。

## 开发脚本

仓库内 `scripts\` 提供一组开发脚本（在命令提示符下直接调用；脚本本身为 ASCII + CRLF）：

| 脚本 | 作用 | 产物 |
| ---- | ---- | ---- |
| `scripts\build.cmd [Debug\|Release]` | 构建整个解决方案（缺省 Release） | `src\NEIDFill\bin\<配置>\net48\` |
| `scripts\test.cmd [Debug\|Release]` | 运行全部单元测试（缺省 Release） | 无 |
| `scripts\ci.cmd` | CI 入口：Release 构建（`-warnaserror`）后用 `--no-build` 跑测试，全部通过打印 `CI OK` | 无 |
| `scripts\publish.cmd` | Release 构建并校验 9 个部署文件后打包发布 | `dist\NEIDFill-<FileVersion>\` 与同名 `.zip` |
| `scripts\clean.cmd` | 只删除 `src`/`tests` 下的 `bin`/`obj` 目录与 `dist\` | 无 |

- 需要 .NET SDK 8.0+（根目录 `global.json` 使用 `rollForward: latestMajor`，允许滚动到更高
  主版本）与 Windows PowerShell 5.1。
- 构建启用 .NET 分析器（Recommended 规则集，规则集固定为 8.0 以免 SDK 升级引入新规则），
  `ci.cmd` 以 `-warnaserror` 执行：任何警告即构建失败；`NEIDFill.Core` 开启 nullable。
- `publish.cmd` 的发布目录只含上文「构建与测试」列出的 9 个部署文件，以及 `README.md`、
  `LICENSE`、`THIRD-PARTY-NOTICES.md`、`CHANGELOG.md`；不含 `.pdb`；`dist\` 已被 gitignore。
- `ci.cmd` 与平台无关：接入任意 CI 时直接调用 `scripts\ci.cmd` 即可（不会 pause，
  非 0 退出码即失败）。
- GitHub Actions 工作流 `.github\workflows\ci.yml` 在 `ci.cmd` 通过后再跑 `scripts\publish.cmd`，
  并把 `dist\*.zip` 作为构建产物上传（每次构建可下载发布 zip）。

## 其它

- `src\NEIDFill\app.ico` 为占位图标（复制自原工程），可随时替换。
