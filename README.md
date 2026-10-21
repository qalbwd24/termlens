# TermLens

TermLens 是一个 Windows 桌面划词阅读助手：在任意窗口选中文本后按快捷键（或鼠标侧键），
它会抓取选中的内容并交给一个大模型解释、追问或润色，结果在浮动窗口里显示，
查过的词自动进生词库，可以朗读、做笔记、回看历史。

面向的场景是读英文论文、文档和网页时不想切窗口查词/查句。

## 功能

- **划词即问**：选中文本后触发，浮窗显示解释；也可在固定输入框里手打。
- **三种模式**：`Explain`（解释/翻译）、`Ask`（追问）、`Polish`（润色改写）。
- **OpenAI 兼容端点**：端点、模型、API Key 都在设置里填，可对接任意兼容 `/chat/completions` 的服务。
  使用系统代理时可直接勾选走 Windows 代理。
- **生词库与历史**：查询记录存在本地 SQLite（`winsqlite3.dll`），支持搜索、只看生词、按时间范围筛选。
- **朗读**：通过微软 Edge 的在线朗读接口（`speech.platform.bing.com`）合成语音，无需本地模型。
- **公式与图片**：数学公式用 WpfMath 渲染；支持把图片贴进来让模型看图。

## 系统要求

- Windows 10 / 11（x64）
- .NET Framework 4.8
- 编译只需要系统自带的 `csc.exe`，**不需要** Visual Studio 或 MSBuild
- 一个 OpenAI 兼容的 API Key（软件本身不带 Key，首次运行在设置里填写）

## 构建

```powershell
.\build.ps1
```

产物是同目录下的 `TermLens.exe`，并把 `lib\` 里的两个 DLL 复制到 exe 旁边。
`build.ps1 -Share` 会额外定义 `SHARE` 并排除 `*Tests.cs`，用于出精简版。

## 运行

```powershell
.\Start-Termlens.ps1              # 正常启动（含全局快捷键与托盘）
.\Start-Termlens.ps1 -NoHotkey    # 不占用快捷键，适合和正式版并存调试
.\Start-Termlens.ps1 -Stop        # 正常退出当前实例
```

程序自身是单实例的：再次启动会唤醒已有窗口而不是开第二个。

## 目录

| 路径 | 说明 |
| --- | --- |
| `src/` | 全部 C# 源码（单文件编译，无 csproj） |
| `assets/` | 图标 |
| `lib/` | `WpfMath.dll`、`XamlMath.Shared.dll` 及第三方声明 |
| `licenses/` | GPL-3.0 全文、edge-tts 许可与声明 |
| `build.ps1` | 用 `csc.exe` 直接编译 |
| `data/` | **运行时生成，已被 .gitignore 排除**：SQLite 历史库、设置、DPAPI 加密的 Key |

`data/` 里含有 API Key（DPAPI 加密）和个人查询历史，不要提交，也不要随安装包分发。

## 许可

GPL-3.0，见 `licenses/GPL-3.0.txt`。
朗读功能依赖 edge-tts 的协议实现，相关声明见 `licenses/edge-tts-*` 与 `lib/THIRD-PARTY-NOTICES.txt`。
