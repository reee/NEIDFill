# libs — 身份证读卡器原生依赖（x86-only）

> **授权 / 再分发（先读）**：以下 5 个 DLL 原样复制自安装目录，NuGet 不可还原，
> **没有厂商再分发授权**。本仓库携带它们仅供内部构建与部署；对外发布、随产品分发或
> 开源前，必须先向厂商取得再分发授权（或改以获得许可的制品库方式分发）。
> 第三方组件声明另见仓库根目录 `THIRD-PARTY-NOTICES.md`。

五个 DLL **原样复制自 NEIDScan 仓库的 `libs\`**（其原始来源为 `D:\Program Files\sfzscan`，
普招身份证采集原程序安装目录），NuGet 不可还原、无厂商再分发授权文件；
均为 32 位（Sdtapi.dll 仅有 x86 版本，因此整个产品按 x86 构建）。

| 文件 | 作用 |
|---|---|
| `Sdtapi.dll` | 华视/精伦读卡接口层。**必须是 sfzscan 版**：只有它导出 `Routon_DecideIDCardType`/`Routon_ReadAllForeignBaseInfosNewPhoto`/`Routon_ReadAllGATBaseInfosPhoto`（外国人永居证、港澳台居住证三类分流），NECapture 仓库里的旧版没有这些导出 |
| `Dewlt.dll` | 正文/照片解码 |
| `SavePhoto.dll` | 照片落盘（Sdtapi 隐式依赖） |
| `WltRS.dll` | WLT 人像解码（Sdtapi 隐式依赖） |
| `JpgDll.dll` | WLT→JPG 转码（Sdtapi 隐式依赖） |

注意：

- Sdtapi 的隐式依赖是**整套**动态加载的，缺任一 DLL 读取会恒返回 -4，五个 DLL 缺一不可；
- 不要混用其它代次的 DLL（如 NECapture `libs/` 里的旧版，导出集不同）；
- 更换读卡器型号/驱动升级时需重新验证三类卡读取。
