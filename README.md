# QuickSearch

QuickSearch 是一个 Windows 10/11 x64 托盘文件夹启动器。复制关键词后按 `Ctrl+Alt+F`：如果关键词已有映射，程序会直接打开它绑定的全部文件夹；如果没有映射，则自动显示窗口并通过 Everything 搜索文件夹。剪贴板为空时也可以直接输入关键词搜索。

## 系统要求

- Windows 10 或 Windows 11 x64
- 安装并运行普通版 Everything 1.4 x64（不要安装 Lite 版，Lite 版不支持 IPC）
- Everything 完成磁盘索引

安装包自带官方 `Everything64.dll` SDK 接口，但不会捆绑或安装 Everything 客户端。Everything 必须在后台运行。

## 使用

1. 从 GitHub Releases 下载 `QuickSearch-Setup-v*.exe` 并安装。
2. 启动 Everything 普通版，等待索引完成。
3. 启动 QuickSearch；关闭窗口后程序仍驻留托盘。
4. 复制关键词，按 `Ctrl+Alt+F`。
5. 如果关键词已有映射，程序不显示确认窗口，直接打开该关键词绑定的全部文件夹。
6. 如果关键词没有映射，程序自动搜索剪贴板关键词；使用 `↑`、`↓` 选择结果，按 `Enter` 打开，也可以双击打开。
7. 如果剪贴板为空，按快捷键会显示空搜索框，输入内容后自动搜索。

设置窗口可以修改全局快捷键、Windows 登录启动，以及手动新增、编辑和删除关键词映射。同一个关键词可以添加多行并绑定多个文件夹；触发该关键词时会一起打开。完全相同的关键词和路径不能重复保存。配置保存在 `%LOCALAPPDATA%\QuickSearch\config.json`。卸载不会删除该配置。

## 本地开发

macOS/Linux 可以开发并运行跨平台核心测试：

```bash
dotnet restore QuickSearch.sln
dotnet test tests/QuickSearch.Core.Tests/QuickSearch.Core.Tests.csproj
```

Windows 上运行完整程序：

```powershell
Invoke-WebRequest https://www.voidtools.com/Everything-SDK.zip -OutFile Everything-SDK.zip
Expand-Archive Everything-SDK.zip -DestinationPath Everything-SDK
New-Item -ItemType Directory -Force src/QuickSearch.Windows/native
Copy-Item Everything-SDK/dll/Everything64.dll src/QuickSearch.Windows/native/Everything64.dll
dotnet run --project src/QuickSearch.Windows/QuickSearch.Windows.csproj
```

## 打包与 Release

GitHub Actions 工作流 `Windows package and release` 会在 `windows-latest` 上运行测试、执行配置读写烟雾测试、发布并检查自包含 `win-x64` 程序、生成 Inno Setup 安装包并上传 Artifact。手动运行工作流并输入 `v0.1.0` 形式的版本号会创建或更新 GitHub Release。

## 故障排查

- 提示 Everything 未运行：确认启动的是普通版而不是 Lite 版，并等待索引完成。
- 快捷键没有响应：在设置里换一个组合，原快捷键可能被其他程序占用。
- 保存路径已失效：程序会打开仍然有效的映射，并显示失效路径；可在设置中修改或删除该行。
- 配置损坏：程序会把旧文件改名为 `config.corrupt-<UTC时间>.json` 并恢复默认配置。
