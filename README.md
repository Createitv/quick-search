# QuickSearch

QuickSearch 是一个 Windows 10/11 x64 托盘工具。复制邮箱别名后按 `Ctrl+Alt+F`，程序会确认已保存的文件夹映射；首次使用时通过 Everything 搜索真实文件夹，确认后自动保存映射并在资源管理器中打开。

## 系统要求

- Windows 10 或 Windows 11 x64
- 安装并运行普通版 Everything 1.4 x64（不要安装 Lite 版，Lite 版不支持 IPC）
- Everything 完成磁盘索引

安装包自带官方 `Everything64.dll` SDK 接口，但不会捆绑或安装 Everything 客户端。Everything 必须在后台运行。

## 使用

1. 从 GitHub Releases 下载 `QuickSearch-Setup-v*.exe` 并安装。
2. 启动 Everything 普通版，等待索引完成。
3. 启动 QuickSearch；关闭窗口后程序仍驻留托盘。
4. 复制邮箱别名，按 `Ctrl+Alt+F`。
5. 首次使用时输入真实文件夹名称，选择结果并点击“确认并打开”。
6. 下次使用相同别名时，确认保存路径后即可打开。

设置窗口可以修改全局快捷键、Windows 登录启动和删除已有映射。配置保存在 `%LOCALAPPDATA%\QuickSearch\config.json`。卸载不会删除该配置。

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
- 保存路径已失效：重新输入文件夹名称并选择新路径，会覆盖旧映射。
- 配置损坏：程序会把旧文件改名为 `config.corrupt-<UTC时间>.json` 并恢复默认配置。
