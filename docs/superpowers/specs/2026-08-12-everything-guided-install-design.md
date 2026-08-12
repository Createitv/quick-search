# QuickSearch Everything 引导安装设计

## 目标

QuickSearch 在 Windows 客户端上自动发现并启动 Everything。只有确认本机未安装 Everything 时，主窗口才显示安装入口；用户点击后使用 QuickSearch 安装包内附带的官方 Everything x64 安装程序完成安装。安装完成后 QuickSearch 自动启动 Everything 后台客户端并恢复搜索，不要求用户手动打开 Everything 窗口。

## 范围

- 支持 Windows 10/11 x64 和 Everything 1.4 x64 普通版。
- QuickSearch 安装包携带一个固定版本的官方 Everything x64 安装程序。
- Everything 使用官方图形安装向导，不执行无人值守安装。
- QuickSearch 卸载时不卸载 Everything。
- 不把 Everything 服务或索引引擎重写、嵌入到 QuickSearch 进程。
- 不支持缺少 IPC 的 Everything Lite 版。

## 运行流程

应用建立单实例后、初始化搜索服务前执行 Everything 可用性检查：

1. 先调用 Everything SDK 探测 IPC；数据库已加载时直接进入正常状态。
2. IPC 不可用时，从 64 位和 32 位卸载注册表、常用 Program Files 目录查找 Everything 普通版可执行文件。
3. 找到可执行文件时，以 `-startup` 参数启动。该参数不显示 Everything 搜索窗口。
4. 在有限时间内轮询 SDK 数据库状态。数据库加载完成后启用搜索；超时则显示“Everything 正在准备索引”，允许稍后自动恢复。
5. 未找到安装记录或可执行文件时，进入 `NotInstalled` 状态并显示安装面板。

Everything Windows 服务本身不作为 SDK 可用性的判断依据。QuickSearch 需要的是当前用户会话中的 Everything 后台客户端 IPC。

## 安装流程

安装面板只在 `NotInstalled`、`InstallerMissing`、`InstallerInvalid` 或安装失败状态出现。正常搜索区域在此期间禁用。

用户点击“安装 Everything”后：

1. 检查 `{app}\dependencies\Everything-Setup.exe` 是否存在。
2. 计算 SHA-256，并与随构建生成的固定值比较。
3. 校验通过后，以 Shell 执行方式启动官方安装程序，使 UAC 和官方安装向导正常显示。
4. 等待安装进程退出，但不阻塞 WPF UI 线程。
5. 无论安装器返回何种退出码，都重新执行安装发现；只有发现 Everything 可执行文件才视为成功。
6. 安装成功后执行 `Everything.exe -startup`，等待 SDK 就绪并自动隐藏安装面板。
7. 用户取消、安装失败或仍未发现 Everything 时，显示可恢复错误和“重试安装”按钮。

同一时间只允许一个安装任务。应用退出时不强制终止已启动的官方安装程序。

## 组件边界

### EverythingInstallationManager

Windows 层服务，负责：

- 从注册表和标准目录发现 Everything。
- 启动 Everything 后台客户端。
- 校验并启动附带的官方安装程序。
- 报告安装过程状态。

该服务通过接口暴露给 UI，进程、注册表和文件系统访问集中在 Windows 项目中。核心状态模型和状态转换放在 `QuickSearch.Core`，便于跨平台单元测试。

### EverythingBootstrapViewModel

负责安装面板的可见性、状态文案、按钮可用性和安装命令。它不直接访问注册表或启动进程。

### App 启动协调

`App` 创建安装管理器和搜索服务，并把两者交给启动协调逻辑。主窗口先创建，未安装时可以立即展示引导；已安装时则保持原来的剪贴板激活和后台启动行为。

## 界面

未安装状态在主窗口搜索区域上方显示一个不嵌套卡片的安装带：

- 标题：`需要安装 Everything`
- 说明：`QuickSearch 使用 Everything 提供本地文件夹快速搜索。`
- 主按钮：`安装 Everything`
- 安装中：`正在等待 Everything 安装完成...`
- 失败状态显示具体原因，按钮文字变为 `重试安装`

安装成功后安装带自动消失。已安装用户不会看到安装入口。安装过程中按钮禁用，搜索框和结果操作禁用，窗口仍可移动、隐藏或退出。

## 打包与供应链

GitHub Actions 使用固定的 Everything 1.4 x64 官方下载地址和固定 SHA-256：

1. 下载官方安装程序。
2. 在 CI 中验证 SHA-256，不匹配则终止构建。
3. 复制到发布目录的 `dependencies\Everything-Setup.exe`。
4. 生成包含同一 SHA-256 的构建常量或清单。
5. 发布检查必须确认 SDK DLL、官方安装器和哈希清单都存在。

源码仓库不提交官方二进制安装器。`THIRD-PARTY-NOTICES.txt` 记录 Everything 程序和 SDK 的版本、来源、版权与许可。升级版本时必须同时更新下载地址、固定哈希和第三方声明。

## 错误处理

- 安装器缺失：提示 QuickSearch 安装不完整，建议重新安装 QuickSearch。
- 哈希不匹配：禁止执行并提示安装文件校验失败。
- UAC 拒绝或用户取消：回到可重试状态，不宣称已安装。
- 安装进程异常：显示简洁错误，保留重试入口。
- 已安装但启动失败：显示启动失败，不错误地显示安装入口。
- Everything 已运行但数据库尚未加载：显示准备状态并继续有限轮询。
- Lite 版或 IPC 永久不可用：说明需要 Everything 普通版。

错误消息不得包含完整异常堆栈或敏感本地信息。

## 测试与验收

核心单元测试覆盖：

- IPC 已就绪时不发现、不启动、不显示安装界面。
- 已安装但未运行时只执行一次 `-startup`。
- 未安装时显示安装入口并禁用搜索。
- 安装成功后自动启动、等待就绪并恢复搜索。
- 用户取消、进程失败、安装器缺失及哈希错误可重试。
- 并发点击不会启动多个安装器。

Windows 构建验证覆盖：

- 注册表 32/64 位视图和标准安装目录发现。
- 发布目录包含安装器与哈希清单。
- CI 下载文件哈希与运行时预期一致。
- 干净 Windows 环境首次启动可看到安装引导。
- 完成官方安装向导后，无需手动打开 Everything 即可搜索文件夹。
- 已安装环境启动时不显示安装引导，Everything 主窗口不弹出。

## 完成标准

在未安装 Everything 的 Windows 10/11 x64 机器上，客户只需安装并打开 QuickSearch，再点击一次“安装 Everything”进入官方安装向导。完成向导后 QuickSearch 自动连接索引并可搜索；后续启动不再出现安装界面，也不需要用户手动打开 Everything。
