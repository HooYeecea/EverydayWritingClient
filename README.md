# Everyday Writing 桌面客户端

Everyday Writing 的 Windows 桌面端。它和网页端共用同一套后端接口，用原生 WPF 实现写作、记录、词库、测评和管理后台，而不是把网页嵌进窗口。

界面为简体中文，视觉上贴近网页端：浅色纸面、侧栏导航、衬线标题和圆角卡片。

## 功能

登录后可以使用：

- 开始写作：选题、富文本草稿、自动保存、正式提交，以及语法、结构和用词反馈
- 写作记录：查看草稿和已提交作文，继续编辑或删除
- 个人词库、个人测评、英语能力测评
- 用户中心：资料、计划、打卡和用量
- 系统设置：主题、编辑器、自动保存和 AI 连接

拥有管理权限的账号可以从侧栏进入管理后台，覆盖数据中心、系统监控、用户与权限、公告和协议、题库、模型供应商、系统配置、访问记录和操作审计。列表类模块在表格中查看，并可用 JSON 新建或修改。

## 环境

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download)（`net10.0-windows`）
- 已启动的 Everyday Writing 后端

默认请求地址是 `http://localhost:5000`，路径前缀为 `/api/v1`。修改地址时改 `EverydayWritingClient/Services/ApiClient.cs` 里的 `Origin`。

登录态保存在本机 `%LocalAppData%\EverydayWriting\session.json`，包含访问令牌、刷新令牌和界面偏好。AI 密钥经后端加密后保存在同一份会话里，请求时通过 `X-Encrypted-Key` 发送。

## 运行

在仓库根目录执行：

```powershell
dotnet build EverydayWritingClient\EverydayWritingClient.csproj
dotnet run --project EverydayWritingClient\EverydayWritingClient.csproj
```

如果系统里有多个 .NET SDK，请使用 10.0 及以上版本。旧版 SDK 无法编译 `net10.0-windows`。

## 目录

```text
EverydayWritingClient/
  App.xaml                 主题、按钮、输入框和表格样式
  MainWindow.xaml          侧栏和页面容器
  Assets/logo.ico          窗口与任务栏图标
  Controls/                标志和富文本编辑器
  Services/                接口、会话、主题和 HTML 转换
  Views/                   登录、写作、记录、词库、测评、设置和管理后台
```

富文本使用 HTML 子集（段落、标题、列表、引用、加粗、斜体、下划线和行内代码），以便草稿与网页端 Tiptap 编辑器互相打开。
