# 第三方组件声明 (Third-Party Notices)

本仓库随附 `libs\` 下 5 个身份证读卡器厂商原生 DLL（均为 32 位）：

| 文件 | 来源/作用 |
| ---- | ---- |
| `Sdtapi.dll` | 华视/精伦读卡接口层（须为 sfzscan 版，导出集见下） |
| `Dewlt.dll` | 正文/照片解码 |
| `SavePhoto.dll` | 照片落盘（Sdtapi 隐式依赖） |
| `WltRS.dll` | WLT 人像解码（Sdtapi 隐式依赖） |
| `JpgDll.dll` | WLT→JPG 转码（Sdtapi 隐式依赖） |

这些 DLL 原样复制自普招身份证采集原程序安装目录，NuGet 不可还原，
**没有任何厂商再分发授权文件**。本仓库携带它们**仅用于内部构建与部署**；
对外发布、随产品分发或开源前，必须先向厂商取得再分发授权，
或改以获得许可的制品库方式分发。

各 DLL 的导出依赖与版本注意事项详见 [`libs\README.md`](libs/README.md)。
除以上 5 个 DLL 外，程序无其它第三方依赖，仅使用 .NET Framework（WinForms）框架组件。
