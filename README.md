# MusicTagClone（音乐标签克隆版）

> 一个使用 C# / WinForms 重新实现的音频标签编辑工具，复刻经典软件 **MusicTag（音乐标签）** 的核心功能，用于批量编辑音频元数据（标签 / 封面 / 歌词）。

## ✨ 功能特性

- **标签编辑**：标题、艺术家、专辑、年份、音轨号、碟号、风格、专辑艺术家、作曲家、作词家、注释、歌词等字段的读写与批量保存
- **封面管理**：多源在线搜索封面（iTunes / 网易云 / QQ音乐 / 酷我 / Last.fm / MusicBrainz / Discogs）、下载、压缩、校验，支持从本地文件导入封面，可提取 / 删除 / 切换内嵌封面
- **歌词下载**：多源搜索与下载歌词（网易云 / QQ / 酷狗 / 酷我），支持原文 + 翻译、KRC / QRC 加密歌词解密、LRC 时间轴格式化、另存为 `.lrc` 文件
- **自动匹配标签**：从多个在线源批量搜索并写入标题 / 艺术家 / 专辑 / 封面 / 歌词，支持多线程、逐字段的写入方式（写入标签 / 写入文件 / 两者都要）与覆盖策略
- **文件名相关操作**：按模板（`@1`–`@8` 占位符）从标签重命名文件、从文件名反写标签
- **编码修正**：对乱码文本提供多种编码方案（GBK、Big5、Shift-JIS 等）预览与修复
- **简繁转换**：简体 ↔ 繁体中文一键转换（含词组级映射）
- **标签历史**：每次保存前自动记录快照，每个文件最多保留 5 条，可随时回滚；封面内容寻址去重存储
- **文件列表**：目录扫描（可含子目录）、关键字 / 格式过滤、多列排序、自定义显示列、拖拽添加、多选批量操作
- **单实例运行**：重复启动时自动聚焦已打开窗口，并可将命令行传入的文件路径转发给已有实例

## 🎵 支持的音频格式

`MP3 · FLAC · M4A/MP4 · OGG · WMA · WAV · AIFF · APE · WV(Monkey's Audio) · MPC · Opus · DSF · DFF`

## 🔧 环境要求

| 用途 | 要求 |
| --- | --- |
| 运行 | Windows（WinForms 程序）；.NET 10 运行时 或 .NET Framework 4.6.1 |
| 编译 | Windows + [.NET 10 SDK](https://dotnet.microsoft.com/download)（`dotnet --version` 需 ≥ 10.0） |

> 程序同时输出 `net10.0-windows` 与 `net461` 两个目标框架。两者都使用 MediaInfo 读取标签（对畸形 MP4 容器更宽容）：`net10.0-windows` 用 `MediaInfo.Wrapper.Core`，`net461` 用 `MediaInfo.Wrapper` + `MediaInfo.Native`；封面/歌词由 TagLibSharp 补充，MediaInfo 解析失败时回退到 TagLibSharp。

## 🛠️ 编译

```bash
# 编译解决方案（同时构建两个目标框架）
dotnet build MusicTagClone.slnx

# 仅编译 .NET 10 目标
dotnet build src/MusicTagClone/MusicTagClone.csproj -f net10.0-windows
```

编译产物位于 `src/MusicTagClone/bin/<Configuration>/net10.0-windows/`（或 `net461/`）。程序会自动把依赖 DLL 移入输出目录下的 `libs\` 子目录（net461 的 `MediaInfo.Wrapper.dll` 需留在根目录，因为其原生库 `MediaInfo.dll` 按该程序集的目录解析，位于根目录 `x64\`/`x86\` 子目录），主程序通过 `AssemblyResolver`（.NET 10）/ `App.config` probing（.NET Framework）加载，直接运行其中的 `MusicTagClone.exe` 即可。

### 运行测试

```bash
dotnet test tests/MusicTagClone.Tests/MusicTagClone.Tests.csproj

# 运行单个测试类
dotnet test tests/MusicTagClone.Tests/MusicTagClone.Tests.csproj --filter "FullyQualifiedName~FileScannerServiceTests"

# 跳过 GUI 自动化测试
dotnet test tests/MusicTagClone.Tests/MusicTagClone.Tests.csproj --filter "FullyQualifiedName!~GUI"
```

> 说明：部分服务测试会访问真实网络 API（封面 / 歌词源），需要联网；`GUI` 下的 UI 自动化测试使用 FlaUI 启动程序，需要先编译 Release 版本并提供测试音频文件。

## 📁 数据文件（位于程序目录下）

| 路径 | 说明 |
| --- | --- |
| `MusicTagClone.db` | SQLite 数据库：应用设置、标签历史记录 |
| `cache\history\` | 标签历史封面（内容寻址存储，不参与自动清理） |
| `cache\img\` | 封面 URL 下载缓存（带 LRU 索引，启动时自动清理） |
| `log\log-YYYY-MM-DD.log` | 运行日志（按天轮转） |

## 🏗️ 项目结构

```
├─ src/MusicTagClone/
│  ├─ Forms/            # WinForms 窗口（主界面、设置、搜索、自动匹配、编码修正等）
│  ├─ Controls/         # 自绘控件（左侧标签编辑面板）
│  ├─ Services/         # 业务逻辑（标签读写、封面/歌词搜索、缓存、历史、日志等）
│  ├─ Models/           # 数据模型与配置序列化
│  ├─ Interfaces/       # 服务接口（依赖注入契约）
│  ├─ ChineseUtils/     # 简繁转换
│  ├─ Utils/            # M4A 标签修复器等工具
│  ├─ Win32/            # 原生 API 互操作（文件夹选择等）
│  └─ Program.cs        # 入口：依赖注入、单实例、全局异常处理
└─ tests/MusicTagClone.Tests/   # xUnit + Moq + FlaUI 测试
```

## 💡 使用要点

- 左侧为标签编辑面板，选中文件后即可编辑；修改后点击「保存修改」写入，可通过「撤销」回退
- 「标签源」菜单可分别配置封面 / 歌词 / 组合标签的搜索源与优先级（设置会持久化）
- 「自动匹配标签」按配置的源顺序批量搜索并合并结果，按标题 / 艺术家 / 专辑相似度排序取最佳
- 搜索到的封面会先经格式、分辨率、大小校验后再写入

## 🙏 致谢

标签读写依赖开源库 **TagLibSharp**、**MediaInfo.Wrapper.Core** / **MediaInfo.Wrapper**；图标来自 **FontAwesome.Sharp**。
