# 寻迹 FileTrace

本地文件搜索索引工具（类似 Archivarius 3000）：为大容量硬盘（1TB 级别）建立文件名 + 全文内容索引，
**硬盘拔出/断开连接后仍可离线搜索，并定位文件在原盘的路径**。面向 Windows 10 中文环境，绿色便携版
（免安装，`data/` 目录随程序一起搬迁）。

## 核心特性

- 支持 doc/docx/xls/xlsx/pdf/ppt 及 txt/md/html/py/java/c/cpp/js/ts/css/json/xml/yaml/ini/cfg/log/sql/bat/sh/ps1/rtf/epub/csv 等扩展文件类型，新建索引时可勾选
- 索引数据可保存到与源文件不同的盘符/位置
- 支持增量索引，无需每次全量重建
- 索引数据保存后，硬盘拔出仍可用文件名 + 内容关键词离线搜索，并显示原始路径
- 搜索语法支持精确短语（`"..."`）、排除词（`-词`）、通配符（`*`）、`filename:`/`content:` 字段前缀
- PPT/PPTX 仅索引文件名/路径/基本信息，不提取全文内容（有意的产品决策）
- OCR 识别默认关闭（可选，会显著降低索引速度）

## 技术栈

- .NET 8 + WPF（`net8.0-windows`），MVVM 用 CommunityToolkit.Mvvm
- 索引/搜索引擎：Lucene.NET 4.8
- 增量扫描指纹库：SQLite（Microsoft.Data.Sqlite），**不是** SQLite FTS5 全文索引
- 旧版 Office 格式解析：NPOI / ScratchPad.NPOI.HWPF；PDF 解析：PdfPig
- 中文分词：jieba.NET

## 项目结构

```
src/
  FileTrace.Core/            索引引擎内核：文件提取器路由、增量扫描、Lucene 封装、SQLite 清单库
  FileTrace.App.Services/    纯 net8.0 类库：服务接口 + Mock/Real 实现（不依赖 WPF，便于跨平台测试）
  FileTrace.App/             WPF 应用外壳：MainWindow/ViewModels/组合根 App.xaml.cs
tests/
  FileTrace.Core.Tests/      Core 层单元测试
  FileTrace.App.Tests/       Services 层集成测试（真实 Lucene/SQLite/文件系统，可在 Linux 上运行）
```

架构要点：`FileTrace.App.Services` 被拆分成独立的纯 `net8.0` 类库（不引用 WPF），
使得对 `RealIndexProfileRepository` / `RealIndexingService` / `RealSearchGateway` 的集成测试
可以在没有 Windows 桌面运行时的环境（包括本仓库的 CI/开发沙箱）里真正**运行**，
而不仅仅是编译通过。`FileTrace.App`（WPF 项目）通过 ProjectReference 引用它。

## 便携版数据布局

```
FileTrace.exe
data/
  registry.json         已知索引的注册表（记录各索引的 StoragePath 列表）
  logs/
    filetrace-2026-07-07.log   按天分文件的运行日志
  <用户自选的索引存储位置>/
    profile.json         该索引的配置元数据
    lucene/               Lucene 索引段文件
    manifest.db           增量扫描指纹库（SQLite）
```

`data/registry.json` 和 `data/logs/` 固定在 exe 所在目录下，整个 `data` 文件夹可以随程序一起
复制到别的电脑/U盘。每个索引自己的存储位置（`profile.json`/`lucene`/`manifest.db`）由用户在
"新建索引"对话框里自由选择，可以在任意盘符——这是支持"索引保存位置与源文件所在盘不同"的关键设计。

## 开发环境构建（Linux/Windows 通用）

项目通过 `EnableWindowsTargeting=true` 使得 `dotnet build` 在没有安装 Windows 桌面工作负载的
Linux 环境下也能正常编译（用于本仓库的沙箱/CI 验证），但**只能编译，不能运行** WPF 界面本身
（这也是为什么 `FileTrace.App.Services` 被拆分为独立类库，好让集成测试能在 Linux 上真正跑通）。

```bash
# 还原 + 编译整个解决方案
dotnet build FileTrace.sln

# 运行全部自动化测试（Core 115 个 + App.Services 17 个）
dotnet test FileTrace.sln
```

## 发布 Windows 便携版

```bash
dotnet publish src/FileTrace.App/FileTrace.App.csproj -c Release -r win-x64
```

产物路径：`src/FileTrace.App/bin/Release/net8.0-windows/win-x64/publish/`

- `FileTrace.exe`：单文件、自包含（内置 .NET 运行时，目标机器无需预装 .NET）
- `Resources/`：jieba.NET 中文分词词典，必须与 exe 放在同一目录下

发布产物即为绿色便携版：将 `publish/` 目录下的全部文件拷贝到任意位置即可运行，首次运行会在
exe 旁边自动创建 `data/` 目录。

> 注：`SelfContained`/`PublishSingleFile` 等发布设置在 `FileTrace.App.csproj` 里通过
> `Condition="'$(RuntimeIdentifier)' == 'win-x64'"` 限定，只有显式传入 `-r win-x64` 发布时才生效，
> 不会影响日常 `dotnet build` 的框架依赖式编译。

## 通过 GitHub Actions 自动发布

`.github/workflows/release.yml` 定义了一个在 `windows-latest` runner 上运行的自动发布流程：
还原依赖 → 跑全部自动化测试 → `dotnet publish -r win-x64 --self-contained true` →
打包成 zip → 创建 GitHub Release 并上传附件。

触发方式二选一：

1. **打 tag 自动触发**（常规发版流程）：
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
2. **手动触发**（无需先打 tag，在 GitHub 仓库页面 Actions → "Build and Release Windows Portable Exe" →
   "Run workflow"，填入版本号如 `v1.0.0` 即可）。

发布产物是 `FileTrace-<version>-win-x64.zip`，解压后即为绿色便携版，可在 Release 页面直接下载。

## 日志与故障排查

程序内置全局异常兜底（UI 线程 `DispatcherUnhandledException` / 后台线程
`AppDomain.UnhandledException` / 未观察的 Task 异常），未捕获的异常会：

1. 记录详细堆栈到 `data/logs/filetrace-yyyy-MM-dd.log`
2. 在 UI 线程上弹出提示对话框（不会导致程序直接崩溃退出）

主窗口左上角"ⓘ"图标可以打开"关于"对话框，查看当前版本号，并一键跳转到日志文件夹。

## 已知限制

- `Microsoft.Win32.OpenFolderDialog` 的真实弹窗交互效果需要在真实 Windows 环境验证
  （Linux 开发沙箱只能验证编译期类型解析，无法弹出真实对话框）。
- `MainViewModel.RunIndexingAsync` 的编排逻辑与 WPF `ObservableObject` 耦合，暂无独立于
  UI 层的自动化测试覆盖。
