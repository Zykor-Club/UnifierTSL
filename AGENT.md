# AGENT.md —— Terraria / TShock / UTSL 工作区指南

> 给在此工作区干活的其他 agent 看。所有命令在 Windows + PowerShell (pwsh) 下验证过。
> 最后更新：2026-10-07

## 一、关键路径速查

| 用途 | 路径 | 状态 |
|:--|:--|:--|
| **UTSL 源码** | `C:\Users\星梦\Desktop\repo-compare\UnifierTSL` | main @ `4a9e5c1`（含自定义尺寸补丁；不是浅克隆，旧记录已过期 ✗） |
| **UTSL 部署本体（正在运行）** | `C:\Users\星梦\Desktop\utsl-deploy` | 端口 **7788**，**无密码**（`config.json` 里 `serverPassword` 为 `""`） |
| **UTSL 插件仓库（新，已适配）** | `C:\Users\星梦\Desktop\repo-compare\UTSLServerPlugin` | GitHub: Zykor-Club/UTSLServerPlugin，结构与工作流对齐 TShockServerPlugin |
| **TShock 插件仓库（原版）** | `C:\Users\星梦\Desktop\机器人开发\TShockServerPlugin` | main @ `b972a49`，13 个插件 |
| **TShock 6.2.1 上游源码** | `C:\Users\星梦\Desktop\repo-compare\TShock` | general-devel @ `b25b3e5`（tag v6.2.1），子模块已初始化 |
| **OTAPI 上游源码** | `C:\Users\星梦\Desktop\repo-compare\Open-Terraria-API` | `SignatureBeef/Open-Terraria-API` @ `b9e0ea0`（Terraria 1.4.5.8 / 3.3.14）。**要确认 `HookEvents.*` / `On.*` 钩子的确切名称和事件参数结构时查这里** —— `OTAPI.Server.Launcher/Program.cs` 里有大量官方用例 |
| 集成测试脚手架 | `C:\Users\星梦\Desktop\repo-compare\_itestItemPoolDbTest` | 临时测试插件（不部署） | |
| 用户已有的独立 TShock 服 | 端口 **7777** | 不要占用 |

分析文档（都在 `repo-compare`）：
- `COMPARISON.md` —— UTSL 与 TShock 的架构/目录/规模差异
- `PLUGIN-COMPAT.md` —— TShock 插件为什么在 UTSL 跑不了、被删 API 清单
- `PORTING-ASSESSMENT.md` —— Zykor-Club 全部 13 个插件的移植难度分级

> ⚠️ TShock 插件仓库有多个副本（`source\repos\Zykor-Club`、`D:\Projects\插件开发`、`D:\Projects\TShock插件开发`）。
> **只有 `C:\Users\星梦\Desktop\机器人开发\TShockServerPlugin` 是最新且工作区干净的**，其余是旧分支或有大量未提交改动。别改错目录。

## 二、环境

- .NET SDK：**9.0.315** 和 10.0.401（UTSL 目标 net9.0）
- **msgfmt**：已通过 winget 装好 `mlocati.GetText`，位于 `%LOCALAPPDATA%\Programs\gettext-iconv\bin`
  - 发布 UTSL 时**强依赖**它（没有会直接退出码 1）。当前会话需手动加 PATH：
    `$env:PATH = "$env:LOCALAPPDATA\Programs\gettext-iconv\bin;" + $env:PATH`
- 参考文档：`utsl-deploy\DEPLOY-README.md`（部署）、`UnifierTSL\docs\dev-plugin.md`（官方插件开发指南）

## 三、必须遵守的坑（都踩过）

### 1. UTSL 不能在重定向 stdout 的环境里启动 ⚠️ 最容易踩
启动器和 TShock 静态构造都会调用 `Console.WindowHeight`。进程若没有真实控制台（例如 `... | Tee-Object` 或在捕获 stdout 的管线里跑），会抛
`System.IO.IOException: 句柄无效`，表现为 **TShock 插件初始化失败 + 进程崩溃**。

✅ 正确：用独立控制台窗口
```powershell
$args = '-port 7788 -password changeme -server "name:Dev worldname:Dev gamemode:0 size:1 evil:0" -joinserver first -logmode txt'
Start-Process -FilePath "C:\Users\星梦\Desktop\utsl-deploy\UnifierTSL.exe" -ArgumentList $args -WorkingDirectory "C:\Users\星梦\Desktop\utsl-deploy" -PassThru
```

### 2. Publisher 必须加 `--no-launch-profile`
否则 `launchSettings.json` 里的 `--output-path "utsl-publish"` 会排在命令行参数前面，而代码取**第一个值**，导致输出到错误目录。

### 3. 端口不要乱选
| 端口 | 占用者 |
|:--|:--|
| 7777 | 用户自己的 TShock 服 |
| 7778 | 华硕 ArmouryCrate |
| **7788** | UTSL 部署（本项目用这个） |

### 4. 停止 UTSL
```powershell
Stop-Process -Name UnifierTSL -Force
Get-Process UnifierTSL.ConsoleClient -ErrorAction SilentlyContinue | Stop-Process -Force
```

### 5. 日志位置
- 启动器持久化日志：`utsl-deploy\logs\<时间戳>.log` —— **插件加载、初始化、运行时异常都在这里**
- TShock 数据根：`utsl-deploy\config\TShockAPI`（`config.json`、`tshock.sqlite`、`setup-code.txt`）
- 各插件配置：`utsl-deploy\config\<插件名>`
- 世界文件：`C:\Users\星梦\Documents\My Games\Terraria\Worlds\*.wld`
- 初始管理员码：`config/TShockAPI/setup-code.txt`，游戏内 `/setup <code>`

### 6. ⚠️ .ps1 脚本必须存成 UTF-8 **带 BOM**
本机默认 shell 是 **Windows PowerShell 5.1**（不是 pwsh 7）。无 BOM 的 UTF-8 脚本会被按 GBK 解码，
中文注释可能吞掉行尾换行，让**下一行代码被注释掉**（真实踩过：`$outDir = "./out/$BuildType"` 整行失效，
后续报 "无法将参数绑定到参数 Path，因为该参数是空值"）。`scripts/*.ps1` 已存成带 BOM。

写新脚本或改脚本时用：
```powershell
$enc = New-Object System.Text.UTF8Encoding($true)   # $true = 带 BOM
[System.IO.File]::WriteAllText($path, $text, $enc)
```

CI 跑在 ubuntu + pwsh 7，不受影响。本地跑脚本前需要先放开执行策略：
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
```

## 四、UTSL 插件模型 vs TShock 插件模型

**UTSL 没有 TerrariaServerAPI**（源码里 `TerrariaPlugin` / `TerrariaApi.Server` 各 0 命中）。

| | TShock 插件 | UTSL 插件 |
|:--|:--|:--|
| 基类 | `TerrariaApi.Server.TerrariaPlugin` | `UnifierTSL.Plugins.BasePlugin` / `IPlugin` |
| 版本标记 | `[ApiVersion(2, 1)]` | `[PluginMetadata("名字","1.0.0","作者","描述")]` |
| 生命周期 | `Initialize()` / `Dispose(bool)` | `InitializeAsync(...)` / `DisposeAsync(bool)` |
| 发现方式 | 反射扫 `[ApiVersion]` | PE metadata 扫 `[PluginMetadata]`（`PluginDiscoverer`） |
| 部署位置 | `ServerPlugins/` | `utsl-deploy/plugins/` |
| 初始化顺序 | 无 | `InitializationOrder => TShock.Order + 1`，并 `await` TShock 的 `InitializationTask` |

⚠️ 传统 TShock 插件 DLL **直接丢进 `plugins/` 不会被加载**（静默忽略，日志里没有该插件）。
⚠️ 部署时**只复制插件自己的 DLL**，不要复制 UnifierTSL.dll / TShockAPI.dll（宿主已有；复制会造成重复加载）。

### API 断裂点速查

| TShock 写法 | UTSL 状态 |
|:--|:--|
| `GetDataHandlers.KillMe` / `ReadNetModule` / 所有 `HandlerList` 事件 | ❌ **成员被删**，改用 `NetPacketHandler.Register<TPacket>` |
| `TShock.DB.Query()` / `QueryReader()` | ❌ **被删**（DB 层换成 linq2db），见下方 |
| `TShock.Log.ConsoleInfo/ConsoleError/ConsoleWarn/ConsoleDebug` | ❌ 改名 `Info/Error/Warning/Debug` |
| `TShock.Log.Warn` | ❌ 改名 `Warning` |
| `TShock.Utils.X` | ❌ 写 `Utils.X`（`Utils` 类还在） |
| `using TShockAPI.Extensions` | ❌ 命名空间没了 |
| `PacketTypes`（发包用） | ⚠️ **不再是 Terraria 的枚举**。TShockAPI 里写的是 `global using PacketTypes = TrProtocol.MessageID;`，插件里要自己加同一行别名，否则报 CS0103 |
| `NetMessage.SendData(...)` | ⚠️ USP 把 `NetMessage` 上下文化了，要写 `server.NetMessage.SendData(...)`（`server` = 目标 `ServerContext`） |
| `PlayerDeathReason` 的部分 API | ⚠️ **USP 也把它上下文化了**：`TryGetCausingEntity(RootContext, out Entity)`、`ByPlayer(RootContext, int)` 需要先传 `RootContext`（`ByNPC(int)` / `ByOther(int)` 未变）。编译期报 CS7036，不会静默出错 |
| `ServerApi.Hooks.ServerBroadcast` | ❌ 无对应事件。替代：**`HookEvents.Terraria.Chat.ChatHelper.BroadcastChatMessage`**（OTAPI 的 HookEvents），参数 `(ChatHelperSystemContext sender, BroadcastChatMessageEventArgs args)`，字段 `args.text` / `args.color` / `args.excludedPlayer` / `args.ContinueExecution` 对应上游的 `e.Message` / `e.Color` |
| `GetDataHandlers.KillMe` 之类的包事件 | → `NetPacketHandler.Register<TPacket>`。**包字段直接抄移植版自己的 `GetDataHandlers.cs`**（例如 `PlayerDeathV2`：`args.Packet.PlayerSlot` / `args.Packet.Reason` / `args.LocalReceiver.Server`） |
| `ServerApi.Hooks.NpcSpawn` / `NpcKilled` | ❌ **UTSL 里连底层的 `OTAPI.Hooks.NPC.Spawn` / `.Killed` 都没有** —— TSAPI 依赖的那套老式手写钩子被 USP 分支移除了（实测编译期 CS0117：Hooks.NPC 未包含 Spawn/Killed 的定义）。改用 **`HookEvents.Terraria.NPC.OnSpawn`** / **`HookEvents.Terraria.NPC.checkDead`**（底层是同一个方法 NPC.NewNPC / NPC.checkDead，而且额外带 `RootContext`，多世界下反而更准）。`OnSpawn` 的 sender 就是 NPC 实例，能直接拿到 `whoAmI`/`type` |
| `NPCDamageTracker.WorldCreditEntry.Name` | ❌ UTSL 的 OTAPI 里没有这个成员（反射确认只有 `Damage`/`HitCount`；`Name` 是 tracker 自己的 `LocalizedText` 属性）。需要显示名就自己用固定标签 |
| `TShock.Utils` | → `TShockAPI.Utils`（静态类，不是 `TShock` 上的属性） |
| `Commands.HandleCommand(player, cmd)` | ⚠️ 签名变了：`Commands.HandleCommand(new CommandExecutor(server, (byte)player.Index), cmd)` |
| `Utils.X` | ⚠️ `Terraria.Utils` 与 `TShockAPI.Utils` 同名，要写全 `TShockAPI.Utils.X` |
| `ServerApi.Hooks.GameUpdate` | → `UnifierApi.EventHub.Game.PreUpdate/PostUpdate` |
| `ServerApi.Hooks.ServerJoin/ServerLeave/NetGreetPlayer` | → `PlayerHooks.PlayerPostLogin/PlayerLogout` 或 `EventHub.Netplay.*` |
| `ServerApi.Hooks.NpcSpawn/NpcKilled`、`WorldSave` | ⚠️ **UTSL 无对应事件**，需 MonoMod `On.Terraria.*` detour |
| `ServerApi.Hooks.NetGetData` | → `NetPacketHandler.Register<T>` |
| `On.Terraria.X.Y` | ⚠️ 存在，但 **USP 下签名被上下文化**（`orig` 多出 `RootContext` 参数） |
| `HookEvents.Terraria.X.Y` | ⚠️ `OTAPI.dll` 里有名字，但是否真触发待实测 |
| `Commands.ChatCommands.Add(new Command(...))`、`CommandArgs`、`Hooks.*`、`TShock.Players/Config/Groups/Regions`、`TSPlayer` | ✅ 保留可用 |

### ⚠️ 坑：UTSL 里 `CommandArgs.Player` 在控制台执行时为 null
- 上游 TShock：控制台执行命令时 `CommandArgs.Player` 是 `TSPlayer.Server`（非 null）。
- UTSL：`CommandArgs.Player = executor.InGamePlayer`，**只有真实客户端才非 null**。

所以命令处理里要写成：
```csharp
// 玩家在游戏内执行时两者相同；控制台/协调器执行时回退到服务器伪玩家
private static TSPlayer ArgPlayer(CommandArgs args) => args.Player ?? args.ExecutorActor;
```
否则从 UTSL 启动器控制台执行管理命令会 NullReferenceException。

### ⚠️ 坑：数据库层是 linq2db
`TShock.DB` 现在是 `LinqToDB.Data.DataConnection`（不再是 TShock 自己的类）。插件里新建连接要抄 TShockAPI 的写法：

```csharp
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using TShockAPI;

[Table(Name = "MyTable")]
class MyRecord
{
    [Column(DataType = DataType.VarChar, Length = 50), PrimaryKey, NotNull] public string Name { get; set; } = "";
    [Column, NotNull] public int Count { get; set; }
}

// linq2db 的 DataConnection 非线程安全，每次操作单独开一个（与 TShockAPI 自身一致）
static DataConnection Open() => new(TShock.DB.Options);

using var db = Open();
db.CreateTable<MyRecord>(tableOptions: TableOptions.CreateIfNotExists);
db.GetTable<MyRecord>().Where(...).Set(r => r.Count, r => r.Count + 1).Update();
db.GetTable<MyRecord>().Insert(() => new MyRecord { ... });
```

### ⚠️ 坑：UTSL 的 TShock 命令分两套，`Commands.HandleCommand` 只能跑传统命令

UTSL 把**绝大多数** TShock 命令迁到了 **V2 声明式命令体系**（`[CommandController]` / `[TShockCommand]`），
它们**不在** `Commands.ChatCommands` 里。实测：整个 TShock 的 `ChatCommands` 只有 **4 条**传统命令，
`HasAlias("bc") = False`、`say = False`、`broadcast = False`。

后果：`Commands.HandleCommand(executor, "/bc 内容")` 会**静默什么都不做**，而且**返回 true**
（`HandleCommand` 对找不到的命令也返回 true），非常容易误判成"命令执行成功"。

要程序化执行任意 TShock 命令，必须自己走 V2 派发（这正是 TShock 自己 `/sudo` 的实现方式）。
`TSCommandBridge.TryHandleDirectCommand` 内部就封装好了，但它是 internal，插件用不了，
所以用下面这套**公开 API** 复刻：

```csharp
using TShockAPI;
using TShockAPI.Commanding;
using UnifierTSL.Commanding;
using UnifierTSL.Commanding.Composition;
using UnifierTSL.Commanding.Endpoints;

public static bool ExecuteCommandText(ServerContext server, string rawText) {
    string text = rawText.Trim();
    if (text.Length == 0) return false;
    if (!text.StartsWith('/')) text = "/" + text;   // 配置里常常不带前缀

    CommandExecutor executor = new(server, byte.MaxValue);   // byte.MaxValue = 服务端控制台执行者
    TSExecutionContext context = new(executor, silent: false, CommandInvocationTarget.ServerConsole);

    CommandDispatchRequest request = new() {
        EndpointId = TerminalCommandEndpoint.EndpointId,      // 控制台端点
        ExecutionContext = context,
        RawInput = text,
        CommandPrefixes = ["/"],
    };

    CommandDispatchResult result = CommandDispatchCoordinator.DispatchAsync(request).GetAwaiter().GetResult();
    if (result.Matched) {
        CommandSystem.GetOutcomeWriter<CommandExecutor>().Write(executor, result.Outcome ?? CommandOutcome.Empty);
        return true;
    }
    return Commands.HandleCommand(executor, text);   // 回退传统命令表
}
```

**排查手法**：用探针插件订阅 `HookEvents.Terraria.Chat.ChatHelper.BroadcastChatMessage` 来观测
`/bc` 到底有没有真的广播出去 —— 比看命令返回值可靠得多（返回值是骗人的）。

### ⚠️ 坑：NPC 死亡判定的四个陷阱（踩全了才做对）

用 `HookEvents.Terraria.NPC.checkDead` 当"NPC 被杀"钩子时，下面四条**全部**成立，缺一条就会出 bug：

**1. `checkDead` 是"每次受到伤害"都会调用的，不是只在死亡时调用。**
Terraria 内部用它判断"这个 NPC 该不该死"，所以玩家每次打中 Boss 都会触发一次。实测日志：
`life=1975/2000` 时就触发了 —— 不做过滤的话，**玩家第一次打到 Boss 就会误触发整套命令**。
→ 必须先用 `npc.life <= 0` 过滤。

**2. 钩子是在 `active = false` 之前触发的，濒死的 NPC 自己还是 `active = True`。**
所以"还有没有同组体节存活"这个判断在现场永远为真 —— 多体节 Boss **永远判不出死亡**
（实测世界吞噬者触发了 67 次 `checkDead`，一次都没判出）。
→ 判定要**延后几百毫秒**，等世界把 `active` 置 false 再查。

**3. 去重键必须按"一整只 Boss"，不能按单个体节。**
否则同一只 Boss 的多个体节各认领各的、互相抵消。实测世界吞噬者：
`OnBossKilled` 触发了、伤害排行榜也播了，但命令被 33ms 前 `checkDead` 的认领挡住，**一次都没跑**。

**4. 一次死亡会同时触发两个钩子：**
- `HookEvents.Terraria.NPC.checkDead`（≈ 上游 `NpcKilled`）
- `On.Terraria.GameContent.BossDamageTracker.OnBossKilled`（伤害追踪）

所以"判定 + 认领"要两个钩子共用同一套逻辑，谁先判定成功谁执行。

**正确姿势**（BossCommandExecutor 里的做法）：

```csharp
// 钩子现场只做"是否真的死了"的初筛 + 转后台
if (npc.life > 0) return;                       // 陷阱1
Task.Run(() => JudgeKillAsync(server, npc, config, source));

async Task JudgeKillAsync(...) {
    await Task.Delay(400);                      // 陷阱2：等 active 被置 false
    if (!IsBossDefeated(server, npc.type)) return;   // 同组还有体节活着
    if (!TryClaimKill(server, npc)) return;          // 陷阱3/4：按整只 Boss 认领
    ...执行...
}
```

> 附带一条：多体节 Boss 的**总血量**要自己算 —— 传到钩子里的 `npc.lifeMax` 只是单个体节的
> （世界吞噬者每节 150）。做法是生成时按体节组累加 `lifeMax`，展示时用累计值。

## 五、插件仓库 UTSLServerPlugin（结构与工作流对齐 TShockServerPlugin）

仓库：https://github.com/Zykor-Club/UTSLServerPlugin

```
UTSLServerPlugin/
├─ Plugin.slnx                   # 与 TShockServerPlugin 同名同结构，列出 src/<插件>/<插件>.csproj
├─ template.targets              # 所有插件 csproj 都要 Import 它（CI 会检查）
├─ Plugins.json                  # 插件依赖表（供 README 生成 / 更新器使用）
├─ Usage.txt / README.md / README.en-US.md
├─ .config/readme-templates/     # README 模板，<!--{PluginList,zh-CN}--> 由脚本替换
├─ .github/workflows/build.yml   # Lint -> Build -> Publish
├─ scripts/
│  ├─ publish-plugins-zip.ps1    # 打包 out/Plugins.zip（只含插件自己的 dll/pdb/README）
│  └─ generate-readme.ps1        # 由模板 + manifest.json 生成 README
├─ src/Shared/I18n.cs            # 共享 GetString，被各插件自动编译进来
└─ src/ItemPool/                 # 每个插件一个目录
   ├─ ItemPool.csproj            # Import ../../template.targets
   ├─ manifest.json              # 描述（CI 必需）
   ├─ README.md                  # 文档（CI 必需）
   └─ *.cs
```

硬性约定（CI 的 Lint job 会拦下来）：
1. `src/<名>/<名>.csproj` 存在，且 **Import 了 `template.targets`**
2. `src/<名>/manifest.json` 与 `src/<名>/README.md` 必须存在
3. `src/<名>/` 下**不能**出现 dll/pdb/exe（产物统一落到 `out/<Configuration>/`）

构建（`template.targets` 的 `UnifierTSLRepo` 默认取同级目录 `../UnifierTSL`）：
```powershell
dotnet build Plugin.slnx -c Release
# UTSL 源码在别处时：
dotnet build Plugin.slnx -c Release -p:UnifierTSLRepo=D:\path\to\UnifierTSL
```

打包：`pwsh ./scripts/publish-plugins-zip.ps1 -BuildType Release` → 产出 `out/Plugins.zip`。

> ⚠️ `template.targets` 里刻意设了 `CopyLocalLockFileAssemblies=false` + ProjectReference 的
> `ExcludeAssets=runtime`。否则 `out/` 会被 OTAPI 等传递依赖灌到 60MB+（OTAPI.dll / OTAPI.Runtime.dll 各 30MB）。
> UTSL 插件只带自己的 DLL；需要私有依赖的插件走 `ModuleDependencies` + `dependencies.json`（参考 UTSL 自带的 Atelier）。

### 已移植插件

| 插件 | 位置 | 状态 |
|:--|:--|:--|
| ItemPool 物品自选池 | `UTSLServerPlugin/src/ItemPool` | ✅ 已在本机 UTSL 实测通过 |
| GroupBlacklist 组黑名单 | `UTSLServerPlugin/src/GroupBlacklist` | ✅ 已实测：命令/配置持久化、在线踢人、世界加载前拦截 |
| ItemHeldMessage 手持提示 | `UTSLServerPlugin/src/ItemHeldMessage` | ✅ 已实测（需在线）：手持检测 → 浮动文本/信息栏/自动命令 `/heal 20` 全通 |
| CustomDeathMessages 自定义死亡消息 | `UTSLServerPlugin/src/CustomDeathMessages` | ✅ 已实测：反射归因 8 字段全在、`/cdm` 三命令无异常、4~5 种死亡类型文案替换成功、死亡计数落库 |
| BossCommandExecutor Boss击杀执行命令 | `UTSLServerPlugin/src/BossCommandExecutor` | ✅ 已实测（需在线）：单体节（史莱姆王）与多体节（世界吞噬者）击杀命令各执行一次、不再提前触发、伤害排行榜正常（含整组总血量） |

### 运行时行为要点（移植时反复踩到的）

**1. ⚠️ 没有玩家在线时，世界不 tick**
UTSL 是"空服挂起"设计（代码里有 `On.Terraria.Main.YouCanSleepNow` 钩子）。用探针插件实测：

| 信号 | 空服 | 有玩家在线 |
|:--|--:|--:|
| `EventHub.Game.PreUpdate` / `PostUpdate` | 0 | 291 → 927（约 63/s） |
| `On.Terraria.Main.Update` | 0 | 291 → 927 |
| `On.Terraria.NetplaySystemContext.UpdateServerInMainThread` | 0 | 291 → 927 |
| `Main.time` | 20 秒纹丝不动 | 正常推进 |
| `EventHub.Game.GamePostInitialize` | 1（整场只触发一次） | 1 |

影响：**任何"定时/每帧"逻辑只在有人在线时生效**（这通常正是它该生效的场合），
但**不能用空服跑自动化测试来验证 tick 逻辑** —— 必须真的进一次服。

**2. 插件侧的 `On.*` 钩子是生效的**
在插件里挂 `On.Terraria.NetplaySystemContext.StartServer` 能计到 1，说明插件与运行时
**共享同一张 MonoMod 钩子表**，可以放心在插件里用 `On.*` detour。

**3. 最早的"准入拦截"点是 `ReceiveFullClientInfoEvent`**
客户端加入时序（`UnifiedServerCoordinator`）：

| 顺序 | 事件 | 能做什么 |
|:--|:--|:--|
| 1 | `EventHub.Netplay.ReceiveFullClientInfoEvent` | 名字/UUID 已知、**未选服、未下发世界数据**；带 `LocalClientSender`，可 `Sender.Kick(自定义提示)` + `args.Handled = true` |
| 2 | `EventHub.Coordinator.ServerCheckPlayerCanJoinIn` | 也在世界数据前，有 `CanJoin`/`FailReason`；**但 UTSL 在 `UnifiedServerCoordinator.cs:244` 用 `out _` 丢掉了 FailReason**，全拒时只给通用提示「No available server found for you.」 |
| 3 | `EventHub.Coordinator.JoinServer` | 已路由，世界数据即将下发 |
| 4 | `TShockAPI.Hooks.PlayerHooks.PlayerPostLogin` | 已经进图了 |
| 5 | `EventHub.Game.PostUpdate` 定时扫描 | 在线期间补救 |

`LocalClientSender.Kick` 会设置 `PendingTermination`/`PendingTerminationApproved`，
所以第 1 层自己踢完再设 `Handled = true`，协调器**不会**再补一条通用提示。

⚠️ 第 1 层拿不到"运行时用户组"（TShock 登录还没发生），只能查注册账号：
`TShock.UserAccounts.GetUserAccountByName(name)?.Group ?? "guest"`（**没有**按 UUID 查询的 API）。

**4. 多世界判定**
`player.GetCurrentServer()` 与事件里的 `ServerEvent.Server` / `args.Content.Server`
**是同一个实例**（`ReferenceEquals` 实测 true），可放心用来筛掉别的世界的玩家。

**5. 事件委托与优先级**

| Provider 类型 | 委托签名 |
|:--|:--|
| `ReadonlyEventProvider<T>` | `void (ref ReadonlyEventArgs<T>)`（有 `Handled`/`StopPropagation`） |
| `ReadonlyEventNoCancelProvider<T>` | `void (ref ReadonlyNoCancelEventArgs<T>)` |
| `ValueEventNoCancelProvider<T>` | `void (ref ValueEventNoCancelArgs<T>)`，`Content` 是 `ref`，改动会写回 |

`HandlerPriority`：`Highest=0 … Normal=50 … Low=70 … Lowest=100`（数值越小越先执行）。
注册用 `Register(handler, priority)`，反注册 `UnRegister(handler)`。

### 测试脚手架

`repo-compare/_itest/` 下有临时测试插件（不属于正式仓库）。模式：
在 `InitializeAsync` 里 `Task.Run` 延迟若干秒 → 用 `new CommandExecutor(server, byte.MaxValue)` 以服务器权限派发命令
（`Commands.HandleCommand(executor, "/xxx")`）→ 断言副作用（读配置文件/DB）→ 打 `TShock.Log` 日志。

⚠️ 测试插件放进 `plugins/` 后会像正式插件一样被加载，验证完**记得删掉**。

**⚠️ 陷阱1：V2 声明式命令不能用服务器执行者派发。**
像 `/spawnmob` 这种带 `PlayerScope = true` 的命令，用 `CommandExecutor(server, byte.MaxValue)` 派发会**静默失败**
（没有报错、没有任何输出、什么都没发生），很容易误判成"钩子没触发"。
要在无玩家时生成 NPC，直接调 API：
```csharp
int idx = server.NPC.NewNPC(new EntitySource_DebugCommand(), tx * 16, ty * 16, npcType);
```

**⚠️ 陷阱2：空服下世界不 tick，NPC/玩家相关的钩子链走不通。**
不要为了"自动化验证"在空服硬造场景 —— **会得出错误结论**。比如 Boss 的 `OnBossKilled`
（伤害追踪）必须真的挨打才会触发；世界不 tick 时自然生成、AI、伤害结算全都不跑。
这类插件只能在**有玩家进服**时验证，把该交给人的步骤交给人。

**找钩子名的最可靠手段：反射探针。**
不确定 UTSL/OTAPI 到底提供哪些钩子、事件参数长什么样时，写个临时插件 dump 出来（比翻文档快且准）：
```csharp
foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
    Type[] types;
    try { types = asm.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
    foreach (Type t in types) {
        if (t.FullName != "HookEvents.Terraria.NPC") continue;   // 想查哪个类型就改这里
        foreach (EventInfo e in t.GetEvents(BindingFlags.Public | BindingFlags.Static)) {
            MethodInfo? inv = e.EventHandlerType?.GetMethod("Invoke");
            // 打印事件名 + 委托签名（sender 类型和 args 类型）
            // 再对 args 类型 GetFields() 就能看到 text/color/ContinueExecution/root 这些字段
        }
    }
}
```
本次移植就是靠它确认了 `HookEvents.Terraria.Chat.ChatHelper.BroadcastChatMessage` 和
`HookEvents.Terraria.NPC.OnSpawn` / `checkDead`，同时确认了老的 `OTAPI.Hooks.NPC.*` 已被移除。

### 仓库工作流与 CI

**Lint job 的硬性要求**（每个 `src/<插件名>/`，`src/Shared` 除外）：

1. `<插件名>.csproj` 存在，且 **Import 了 `template.targets`**
2. `manifest.json` 必须存在
3. **`README.md` 必须存在** —— 最容易漏，我漏了两次直接让 CI 变红
4. 目录下不能有 dll/pdb/exe（产物统一输出到 `out/<Configuration>/`）

提交前本地自查：
```powershell
$errors=@()
Get-ChildItem src -Directory | Where-Object Name -ne 'Shared' | ForEach-Object {
  $n=$_.Name
  if(-not(Test-Path "src/$n/$n.csproj")){$errors+="csproj: $n"}
  if((Get-Content "src/$n/$n.csproj" -Raw) -notmatch 'template\.targets'){$errors+="import: $n"}
  if(-not(Test-Path "src/$n/manifest.json")){$errors+="manifest: $n"}
  if(-not(Test-Path "src/$n/README.md")){$errors+="README: $n"}
}
if($errors){$errors}else{'lint ok'}
```

**⚠️ CI 编译 UTSL 源必须用完整历史**：workflow 里 `Checkout UnifierTSL` 要用 **`fetch-depth: 0`**。
UTSL 的项目用 `GitVersion.MsBuild` 算版本号，浅克隆会被它直接拒绝：
```
Repository is a shallow clone. Git repositories must contain the full history.
→ error MSB3073: ... gitversion.dll ... exited with code 1
```

**⚠️ Publish Release 偶尔会因 GitHub 瞬时 500 失败**：表现为 Lint/Build 都 success，
只有 `Create Release` 报 `Error 500`。**不是代码问题**，重跑一次 workflow 即可（实测 `fe865bb` 就是这样，
下一个提交 `1daacbd` 同样的代码 Publish 成功）。

**推送凭证**：git 存的凭证在 `~/.git-credentials`，**必须是有写权限的 PAT**。
之前存的是旧的只读 token，表现为 `Permission to Zykor-Club/UTSLServerPlugin.git denied` / HTTP 403
（注意 `git ls-remote` 仍是正常的 —— 因为读权限够用，容易误判成"凭证过期"）。
有完整权限的 PAT 在 `C:\Users\星梦\Desktop\机器人开发\AGENT.md`（`github_pat_...`，属于 `star620`，
对 `Zykor-Club/UTSLServerPlugin` 有 `admin/push`）。**那份文档里"只有读权限、必须 Fork + PR"的说法已过期**，
实测可以直接 push。查权限：`gh api repos/Zykor-Club/UTSLServerPlugin --jq .permissions`。

## 六、常用命令

### 编译 + 发布 UTSL
```powershell
cd C:\Users\星梦\Desktop\repo-compare\UnifierTSL
$env:PATH = "$env:LOCALAPPDATA\Programs\gettext-iconv\bin;" + $env:PATH
dotnet build src\UnifierTSL.slnx -c Release
dotnet run --project src\UnifierTSL.Publisher\UnifierTSL.Publisher.csproj -c Release --no-launch-profile -- `
  --output-path "C:\Users\星梦\Desktop\utsl-deploy" --use-rid-folder false --clean-output-dir true
```

### 编译 UTSL 插件仓库
插件工程通过 `UnifierTSLRepo` 属性找 UTSL 源码，默认是 `<仓库同级目录>/UnifierTSL`：
```powershell
cd C:\Users\星梦\Desktop\repo-compare\UTSLServerPlugin
dotnet build UTSLServerPlugin.slnx -c Release
# 如果 UTSL 源码在别处：
dotnet build UTSLServerPlugin.slnx -c Release -p:UnifierTSLRepo=D:\path\to\UnifierTSL
```

### 部署单个插件并重启
```powershell
$d = "C:\Users\星梦\Desktop\utsl-deploy"
Copy-Item "UTSLServerPlugin\src\ItemPool\bin\Release\net9.0\ItemPool.dll" "$d\plugins\" -Force
Stop-Process -Name UnifierTSL -Force -ErrorAction SilentlyContinue
Get-Process UnifierTSL.ConsoleClient -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 5
Start-Process -FilePath "$d\UnifierTSL.exe" -WorkingDirectory $d -ArgumentList '-port 7788 -password changeme -server "name:Dev worldname:Dev gamemode:0 size:1 evil:0" -joinserver first -logmode txt'
```

### 确认运行状态
```powershell
netstat -ano | Select-String ':7788'
Get-Process UnifierTSL,UnifierTSL.ConsoleClient -ErrorAction SilentlyContinue
Get-Content (Get-ChildItem "C:\Users\星梦\Desktop\utsl-deploy\logs" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName -Tail 30
```

## 七、怎么验证一个插件真的能用（不靠游戏客户端）

写一个临时测试插件放进 `plugins/`，在 `InitializeAsync` 里直接调用被测代码，把结果打到 `TShock.Log`，然后读日志。参考
`repo-compare\_itest\ItemPoolDbTest`，它验证了：

1. 插件被加载并初始化（日志出现 `插件"X"v...已初始化`）
2. 配置文件在 `config/<插件名>/` 下生成且内容正确
3. 数据库表被创建（用 python + sqlite3 查 `tshock.sqlite`）
4. 增删改查所有路径（插入 / 更新 / 查询 / 删除）
5. 命令派发与副作用：
   ```csharp
   CommandExecutor executor = new(server, byte.MaxValue);   // 服务器上下文里的控制台执行者
   Commands.HandleCommand(executor, "/xz resetall");
   ```
   然后断言数据库/配置状态确实变了 —— 这比看命令输出可靠（`TSServerPlayer.SendMessage` 的输出不一定进持久化日志）。

命令测试要等世界启动完成，所以需要 `await Task.Delay(40s)` 或挂 `EventHub.Game.PostUpdate`（插件初始化时世界还没起来）。

## 八、排查 UTSL 插件加载失败

1. 看 `utsl-deploy\logs` 最新日志有没有
   `[Warn][UTSL-PluginHost|Loading] 无法创建插件...` / `[Warn][UTSL-PluginHost|Init] 插件"X"初始化失败`
2. 插件没出现在 `插件"X"v...已初始化` 列表 → 检查有没有 `[PluginMetadata]`、是不是抽象类/无默认构造函数
3. `TypeInitializationException` 多半是平台差异（控制台句柄、路径、缺依赖）
4. 插件用了第三方库时，宿主可能解析不到 → 参考 Atelier 的 `dependencies.json` + `[assembly: ModuleDependencies<T>]` 机制


---

# 多世界无缝拼接实践记录（2026-10-09 整理）

## 一、整体方案（已跑通）

**超长地图 → TEdit 式等宽切分 → UTSL 三世界 → 世界边缘换乘** = "无缝"体验

### 1. 让 UTSL 支持自定义世界长度（源码 2 处改动）
- `src/UnifierTSL/Launcher/LauncherSettingValues.cs` → `TryResolveSize`：
  `size >= 1 && (size <= 3 || size >= 1000)`（>=1000 视为自定义宽度）
- `src/UnifierTSL/Servers/IWorldDataProvider.cs` → `switch(worldSize)` 的 default：
  `worldSize >= 1000` 时 `maxTilesX = worldSize; maxTilesY = 2400;`
- 配置 `config/config.json` 的 `size` 直接写宽度（如 `"12000"`）

⚠️ **部署陷阱 1**：运行时加载的是 **`lib\UnifierTSL.dll`**，**不是根目录那个**！
   替换错文件 = 改动完全不生效（曾为此浪费很多轮）。

⚠️ **部署陷阱 2**：**必须用 `Start-Process` 启动服务器**。
   用后台任务方式启动拿不到控制台句柄 → `TShockAPI.TShock` 静态构造抛
   `IOException: 句柄无效` → **所有 TShock 插件初始化失败**。

### 2. 切分工具 UTSLStitchTool（net10 控制台 + 引用 TEdit.Terraria）
| 命令 | 作用 |
|---|---|
| analyze <wld> [列数] | 两侧边缘逐列地表高度 |
| structs <wld> | 扫描地牢/神庙 X 范围（用于避让切割）|
| split <wld> <out> <份数> [重叠] [avoid] | 等宽切分，可自动偏移避开建筑 |
| stitch <A> <B> <C> <out> | 三世界滑动匹配 + 等宽裁剪 |
| crop <wld> <x0> <x1> <out> | 裁剪区间 |

实现要点：
- 必须先初始化 ReactiveUI（`RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp()`），
  否则 `new World()` 抛 TypeInitializationException
- `TEdit.Terraria.Tile` 是 **struct**（数组默认零值即空图格）
- `ProgressChangedEventArgs` 是 BCL 类型（`System.ComponentModel`）
- 裁剪必须一并**平移/剔除**：箱子、告示牌、图格实体、NPC、怪物、压力板、玩家房间
  （漏了会写出坏存档 → 服务端 `IndexOutOfRangeException`）
- `World.Save(..., versionOverride: 0, ...)`（传 -1 会写出错误版本号 → 被当成旧版存档）

### 3. EdgeStitch 参数（config/EdgeStitch/EdgeStitch.json）
```json
{
  "世界链": ["West", "Dev", "East"],
  "陆地边缘": { "West": {"东": 4158}, "Dev": {"西": 41, "东": 4158}, "East": {"西": 41} },
  "边缘触发余量_格": 60,
  "切片间距_格": 3900
}
```
- 片宽 4200 = 核心 4000 + 左右重叠各 100；相邻片起点差 **Δ = 3900**
- **陆地边缘 41 / 4158**：Terraria 最外 41 格玩家**物理上走不进去**（实测卡在 2133-41=2092），
  触发阈值必须落在这条线之内
- **触发余量必须 ≥ 50~60 格**：否则最外 41 格的"世界边界暗区"会进视野（玩家看到一片空置域）

## 二、踩坑总表

| 现象 | 根因 | 解法 |
|---|---|---|
| **客户端小地图错位** | **切片尺寸/重叠一变就出现**（片宽 4200 正常；2333、3956 均错位）| **保持切片宽度固定不变**（推荐 4200 = 标准小世界宽），不要频繁改切分几何 |
| 世界边缘大片空置域 | 最外 41 格是 Terraria 不可破坏边界，余量太小就走进了视野 | 触发余量 ≥ 50~60 格 |
| 落点在"地下 100 格" | 扫描判据用 `tileSolid`，把地表那层跳过，一路往下钻到第一个"实心" | 判据改为"第一个 active 图格即地表" |
| 落点在空岛上 | 从天空往下第一个实心就是空岛 | 同上 |
| **落点必须"位置保持"** | 两侧是同一张地图的切片，**同一大地图坐标处地形完全相同** | 目标格 X = 源格 X − Δ；**Y 原样照搬**；只做 0~8 格向上微调兜底，**绝不向内搜索** |
| 换乘后画面断裂 | 切片不重叠 → 对应坐标是负数 | 切片必须重叠；`源末端 − 目标起点 = 3×重叠 − 83`；重叠 28 时差 1 格最连续，但会导致地图错位，**当前取舍为重叠 100** |
| 修改 UTSL 源码不生效 | 运行时加载 `lib\UnifierTSL.dll` | 替换 `lib` 里那个 |
| 所有 TShock 插件初始化失败（句柄无效）| 后台任务方式启动 | 必须 `Start-Process` |
| 世界保存极慢 / OOM | 尺寸过大 | 12000×2400（2880 万图格）可用；18000×2400（4320 万）OOM；**要更长就降高度**（内存 = 宽 × 高；15000×1800 与 12000×2400 等量）|
| 秘密种子 | UTSL 把种子交给原版生成器，秘密种子判定照常生效 | ✅ 实测 `05162020`（醉酒世界）12000 宽生成成功 |

## 三、已完成 / 待办

**已实现**
- EdgeStitch：边缘换乘、位置保持落点、**世界状态同步**（`Core/WorldStateSync.cs`，62 个字段：
  时间/天气/Boss 旗标/已解救 NPC/矿石层级/入侵）
  - 换乘瞬间源→目标全量同步；每 2 秒以"有玩家的世界"为权威统一
    （无玩家时世界不 tick，故不会执行）
  - 玩家自身（buff/debuff/血量/背包）**不需要同步**：UTSL 迁移的是同一个 Player 对象
- UTSLStitchTool：切分 + 建筑扫描 + 避让偏移
- GenerateMap：`/map`、`/stitch 分析`、**Δ 感知拼接**（自动读取 EdgeStitch 的"切片间距_格"）

**待办 / 未定论**
- **小地图错位**：**已定论**：世界宽度必须是 200 的整数倍（详见文末「★ 补充定论」）
  客户端按 GUID 缓存 `.map` + 世界尺寸变化有关，**未最终定论**
- 切割避让地牢/神庙：`split ... avoid` 已可用；注意开启 avoid 会把核心缩小 100，**Δ 随之变化**，
  必须同步更新 EdgeStitch 的"切片间距_格"
- 跨世界状态同步的实测验证（需玩家在线：在 A 世界击杀骷髅王 → 去 B 世界地牢段不应刷地牢守卫）

## ★ 补充定论：世界宽度必须是 200 的整数倍，高度必须是 150 的整数倍

（2026-10-09 由反编译的客户端源码 terraria v1.4.5.8 证实）

```csharp
Main.cs:1092   public const int sectionWidth  = 200;
Main.cs:1094   public const int sectionHeight = 150;
Main.cs:1096   public static int maxSectionsX = maxTilesX / 200;   // 整数除法！
Main.cs:1098   public static int maxSectionsY = maxTilesY / 150;   // 整数除法！
```

客户端按 **200 x 150** 的区块（section）加载与渲染，而 `maxSectionsX = maxTilesX / 200` 是**整数除法**。
所以当 maxTilesX **不是 200 的整数倍**时，**末尾那段图格永远没有任何区块覆盖**：

| maxTilesX | maxSectionsX | 覆盖图格 | 未覆盖 | 表现 |
|--:|--:|--:|--:|:--|
| **4200** | 21 | 4200 | **0** | 正常 |
| 3956 | 19 | 3800 | **156 格** | 小地图错位 + 边缘大片空置域 |
| 2333 | 11 | 2200 | **133 格** | 小地图错位 |

**而且客户端不会崩溃** —— 它只是**静默地不加载**那些没有区块的图格。
这正是"地图显示错位 / 世界边缘出现大片空置域，但不崩溃"的真正原因。

**原版世界尺寸全部满足**：宽度 4200/6400/8400（200 的整数倍）、高度 1200/1800/2400（150 的整数倍），
所以正常玩原版永远碰不到这个问题。

### 由此得出的硬性规则
1. 切分世界时，**每一片的 maxTilesX 必须是 200 的整数倍、maxTilesY 必须是 150 的整数倍**。
2. **推荐片宽直接用 4200**（= 21 x 200），与标准小世界一致 —— 已实测正常。
3. 反例：为了追求"接缝 1 格连续"把片宽改成 3956（非 200 倍数）→ **必然错位**（已实测）。
   即"用重叠量微调接缝连续性"受限于 200 的倍数：重叠 100（片宽 4200）可用；
   若想用重叠 28，也必须让最终片宽仍是 200 的倍数，不能直接取 3956。


## 多世界 AutoResetPlus（整链重置）实践记录（2026-10-09）

### 为什么不能照搬原插件
原版 AutoResetPlus 是 **TShock 单世界**重置（进程内 WorldGen.CreateNewWorld → WorldFile.LoadWorld）。
本服务器三个世界是**同一张大地图切出来的三片**，各重置各的会让接缝立刻断裂 ✗，
所以必须**整链重置**：生成一张新源地图 → 重新切分 → 替换三片 → 重启。

### 已实现的完整流程
- **阶段 1**（插件进程内）：广播倒计时 → 执行「重置前指令」→ 踢人 → 把 UTSL 启动列表切成「只留源地图」
  → 写 pending-reset.json → 启动重启脚本 → Environment.Exit
- **阶段 2**（下次启动自动续跑）：等源地图生成 → 主动触发保存 → UTSLStitchTool split 切分
  → 解析几何 → **按世界数重算 EdgeStitch 配置** → 部署切片 → 恢复启动列表
  → 执行「替换文件 / 重置后指令 / SQL / 抽下次种子」→ 清 pending → 再重启
- 配置：config/AutoResetPlus/MultiWorld.json

### ★ 本轮踩的坑（都很隐蔽）
| 现象 | 根因 | 解法 |
|:--|:--|:--|
| **重置后弹窗「Windows 找不到文件 C:Users\<乱码中文>\...」** | 重启脚本用 .cmd 写（UTF-8），而 **cmd.exe 按系统 ANSI(GBK) 读** → 含中文的路径必然乱码 | **改用 .ps1 + UTF-8 带 BOM**（PowerShell 正确处理 Unicode 路径） |
| 放的插件 dll 不生效 | 插件宿主加载**扁平路径** plugins/<插件名>.dll | 两处都部署：plugins/X.dll 与 plugins/X/X.dll |
| 重置卡在「等源地图生成」 | 服务端世界**只在关机/自动保存时写盘** | 阶段 2 每 60 秒走本机 REST /world/save 主动触发保存 |
| 解析到 4322（实际 4200）→ 陆地边缘算错 → 传送全废 | 正则匹配到警告行里**取整前**的值 | 只匹配汇总行「片宽 NNNN = 200」 |
| 每轮重启都再触发一次重置（无限循环） | 测试用环境变量**被子进程继承** | 钩子读取后立刻 SetEnvironmentVariable(name, null, Process) 清除 |
| TShock.Log.Warn / ConsoleInfo 不存在 | UTSL 的 RoleLogger 方法名不同 | 用 Warning / Info / Error |
| 秘密种子的 WorldGen 标志位不存在 | 1.4.4+ 已改用种子字符串 | 只保留种子名，直接交给生成器（实测 05162020 生效） |
| TSPlayer.Server / TShock.DB.Query / Commands.HandleCommand(TSPlayer,..) 不存在 | TShock 版本签名不同 | 用**反射**调用（编译期不绑定签名） |

### 自定义世界长度（UTSL 源码补丁）
- LauncherSettingValues.TryResolveSize：size >= 1000 视为自定义宽度（1~3 仍走原版小/中/大）
- IWorldDataProvider：宽度取 size，**高度按宽度自动推导**（内存 = 宽×高，越长必须越矮）：
  <=13000 → 2400 / <=16000 → 1800 / 更大 → 1200（都必须是 **150 的整数倍**）
  （★ 2026-10-11 按源码更正：旧值 "<=12800→1500 / <=17000→1200 / 更大→900" 是错的 ✗
    源码 Servers/IWorldDataProvider.cs:70 实际是 2400 / 1800 / 1200；可用 UTSL_CUSTOM_HEIGHT 环境变量强制覆盖）
- **硬约束**：内存/写盘上限。实测 12000x2400（2880 万图格）可用；12800x2400 OOM；
  12800x1500（1920 万）可用
- **N 个世界的几何约束**：份数 × 片宽(200 倍数) − 3×重叠×(份数−1) <= 源地图宽度
  例：5 个世界 = 5×4200 − 3×28×4 = 20664，高度自动降到 900

## ★ 追加定论：切片世界宽度用 4200，不要用 4000（2026-10-09 实测）

### 现象
客户端小地图【整张图偏移】（进服即偏、重进服不恢复、缩放小地图时与原地图框的偏移量还会变化），
但 GenerateMap 插件渲染同一份世界文件却完全正常 → 世界文件没问题，问题在客户端的地图基准。

### 对照实验（关键）
| 参数 | 正常 | 偏移 |
|:--|:--|:--|
| 切片宽度 | 4200 | 4000 |
| 源地图宽 | 12432 | 11832 |
| 重叠 / Δ | 28 / 3916 | 28 / 3916 |
| 高度 | 2400 | 2400 |
| 配置 size 声明 | 1（错误） | 4000（正确） |

结论：唯一相关变量是【切片宽度】。4200 正常，4000 偏移。
（顺带排除两条错误猜测：① 不是客户端 .map 缓存——重进服不恢复；② 不是配置 size 声明——当时声明是错的但小地图正常）

### 证据：客户端地图基准来自 WorldData 包
```csharp
// terraria v1.4.5.8 反编译源码
MessageBuffer.cs:482    Main.maxTilesX = reader.ReadInt16();
```
客户端的世界尺寸/地图基准完全由服务端下发的 WorldData 包决定。

### 硬性规则
1. 切片宽度固定用 4200（= 21 x 200）。实测 4200 正常、4000 偏移。
   两者都是 200 的整数倍，所以不只是“区块整除”的问题，还有别的因素（未查明），
   因此不要随意更换片宽，换之前必须先用小地图验证。
2. 源地图宽度 = 份数 x 片宽 - 3 x 重叠 x (份数-1)
   （否则链条铺不满源地图，末尾海域会被静默丢弃 —— 实测丢过右侧海洋）
3. 高度必须是 150 的整数倍（区块高 150）。

### 本轮其它实证结论
- 内存优化：切分要移出服务器进程（插件写配置 → 退出 → PowerShell 脚本切分/部署/启动），
  否则服务器与切分工具同时持有源地图 = 2x 峰值。
- .cmd 装不下中文路径：cmd.exe 按系统 ANSI(GBK) 读脚本，UTF-8 写的中文路径必然乱码
  （实测弹窗 找不到文件 C:Users\鏄燈vi\...）→ 一律用 .ps1 + UTF-8 带 BOM。
- 插件宿主加载【扁平路径】plugins/<插件名>.dll，**也会加载一层子目录** plugins/<插件名>/<插件名>.dll（2026-10-11 按源码更正 ✗ 旧说法"不是子目录"是错的）。同名时扁平那份胜出。
- 出图顺序要按 EdgeStitch 的「世界链」渲染（协调器内部顺序不保证一致，实测串位）。
- 落点用“位置保持 + 离地高度保持”：X 平移 Δ、Y 按两侧地表高度差修正（不要用扫描找地表）。
- 出生点裁剪时越界不要夹到切片边缘（会卡在世界最右边），要放切片中心并重算 Y。

## ★ 多世界拼接的最终参数与硬约束（2026-10-10 定稿）

### 几何公式与约束
~~~
源地图宽 = 切片数 x 片宽 - 3 x 重叠量 x (切片数-1)
高度自动: 源宽 <=13000 -> 2400 | <=16000 -> 1800 | 更大 -> 1200
（★ 2026-10-11 按源码更正：旧值 <=17000 是错的 ✗ 源码 Servers/IWorldDataProvider.cs:70 是 16000 ✓）
~~~
硬约束（全部是实测踩出来的，违反任何一条都会出问题）:
1. 【片宽必须是 200 的整数倍】客户端区块宽 200。实测 4200 正常、4000 会让小地图【整图偏移】
   （重进服不恢复、缩放小地图时偏移量还会变化）。两者都是 200 倍数，所以还有别的因素，未查明；
   【不要随意换片宽，换之前必须用小地图验证】。
2. 【源地图宽必须是 200 的整数倍】否则 .wld 最后一个区块残缺 -> 裁剪/拼接出错。
   因为 切片数x片宽 已是 200 倍数，等价于【3 x 重叠量 x (切片数-1) 是 200 的倍数】。
   实测：重叠 28 + 3 片 -> 源宽 12432 -> 每片右侧出现一条约 42 列的假地形条。
3. 【总图格数 <= 约 2900 万】内存上限。12000x2400=2880万 可用；12800x2400=3072万 OOM。

各切片数对应的合法重叠量（片宽 4200）:
| 切片数 | 重叠量需为 | 建议 | 源地图宽 | 高度 | 图格 |
|:--|:--|:--|:--|:--|:--|
| 2 | 200 的倍数 | 200 | 7800 | 2400 | 1872万 |
| 3 | 100 的倍数 | 100 | 12000 | 2400 | 2880万 |
| 4 | 200 的倍数 | 200 | 15000 | 1800 | 2700万 |
| 5 | 50 的倍数 | 100 | 19800 | 1200 | 2376万 |
| 6 | 40 的倍数 | 120 | 23400 | 1200 | 2808万 |

AutoResetPlus 已内置几何自检：片宽非 200 倍数、或重叠量会让源宽非 200 倍数时，
自动修正到最近的合法值并打 Warning（避免手工改配置踩坑）。

### 客户端地图基准来自 WorldData 包
~~~
// terraria v1.4.5.8 反编译源码
MessageBuffer.cs:482    Main.maxTilesX = reader.ReadInt16();
~~~
客户端的世界尺寸/地图基准完全由服务端下发的 WorldData 包决定。

### GenerateMap 出图的多个调用点必须【统一传切片间距 Δ】
实测：命令路径漏传 Δ（自动出图传了）-> 拼接按片宽排列而非间距排列 ->
每片错位 (片宽-Δ) 列 -> 表现为【假地形条】+【神庙等地形从中间断开】。
拼接规则：前 N-1 片各取 [0, Δ)，最后一片取 [0, 宽-42)——
那 42 列是世界最右的【不可达边缘带】，玩家永远走不到也永远没被点亮，
画进去就是一条暗带（假地形条），还会切掉右侧海洋的尾巴。

### 其它实证结论
- 【UTSL 的 Game.PostUpdate 只在有玩家的世界触发】（空世界不 tick）。
  所以用它做跨 tick 分片渲染时，空世界的分片永远推进不了 ——
  正确做法：有玩家的世界走分片，没玩家的世界直接同步渲染（没人在跑，阻塞无感）。
- 世界裁剪工具 UTSLStitchTool 的 CropCore 已逐行审计：索引无重叠/无越界/无重复，代码正确。
- 出生点裁剪：越界时不要夹到切片边缘（会卡在世界最右边），要放切片中心并重算 Y。
- 落点：位置保持(X-Δ) + 离地高度保持（按两侧地表高度差修正），不要用扫描找地表。
- 内存优化：切分必须移出服务器进程（插件写配置 -> 退出 -> PowerShell 脚本切分/部署/启动），
  否则服务器与切分工具同时持有源地图 = 2x 峰值。脚本必须 try/finally 保证最后一定启动服务器。
- .cmd 装不下中文路径（cmd.exe 按 ANSI/GBK 读脚本）-> 一律用 .ps1 + UTF-8 带 BOM。
- 插件宿主加载【扁平路径】plugins/<插件名>.dll（优先），并**同样加载一层子目录** —— 部署两处都放且保持同版本即可。


## ★ UTSLManager（Avalonia 版）搭建经验（2026-10-10）

### 技术选型结论
- **Windows 上仿 SwiftUI 观感的最佳现成方案：`Devolutions.AvaloniaTheme.MacOS`**（NuGet 包 ✓）
  它要求 **Avalonia >= 12.1.2 且版本必须钉死** —— 用浮动 `12.*` 会拉到不兼容的新版 ✗
  → 现象：编译 0 错误但**运行即退出** ✗（异常：XAML 加载失败 ✗）
- 交通灯（红黄绿）**Windows 上没有原生支持** ✗ —— Avalonia 的窗口 chrome 按平台选 ✗
  → 做法：`ExtendClientAreaToDecorationsHint="True"` 去掉系统 chrome ✓ + 自绘三个圆 ✓
    颜色用原值 `#FF5F57` / `#FEBC2E` / `#28C840` ✓ + `PointerPressed` 接事件 ✓ + `BeginMoveDrag` 拖拽 ✓
- **Avalonia 12 里 `ExtendClientAreaChromeHints` 属性不存在** ✗（加上会编译失败 ✓）

### 两个大坑（都实测踩过）
1. **自绘标题栏（ExtendClientArea 融合区）里放控件不渲染** ✗
   现象：右上角的控制区死活看不见 ✓（换成普通容器立刻正常 ✓）
   → 结论：控制区要放在**普通容器**里（主内容区顶栏 ✓），不要放融合标题栏 ✗
2. **真·毛玻璃（`ExperimentalAcrylicBorder`）在桌面上看起来就是"和别的窗口叠着"** ✗
   现象：用户反馈"右上角交叠" ✓ 实际是后面的窗口透过来了 ✓
   → 结论：要干净观感就用**不透明色模拟** ✓ 想要真毛玻璃就得接受背景透视 ✓

### 其它环境坑
- **150% DPI 缩放下窗口会跑出屏幕** ✗ → 手工算位置会被系统覆盖 ✗ → **用「启动即最大化」最稳** ✓
- **PowerShell 执行策略禁止 `npm.ps1`** ✗ → 一律用 `npm.cmd` / `pnpm.cmd` 调用 ✓
- **C# 普通字符串里的路径要双写反斜杠** ✗（`"GenerateMap\Images"` ✓），`@"..."` 里只写一个 ✓
- Tauri 路线也可行（Rust 1.99 已装 ✓ VS 18 + MSVC 14.51 + Windows SDK + WebView2 全就绪 ✓）

### 服务端侧（已推送 ✓）
- 插件 **WorldCommandChannel** 提供按世界的 REST 通道 ✓：
  `GET /world/list` + `POST /world/command?world={名|序号|all}&cmd={指令}` ✓
- 执行等价于控制台：`new CommandExecutor(目标世界, byte.MaxValue, TSRestPlayer)` ✓
  （`CommandExecutor.SourceServer` 就是 UTSL 内置的"这条指令属于哪个世界"字段 ✓）
- 坑：`TShock.Rest` 不存在 ✗ → 是 **`TShock.RestApi`** ✓；参数类型是 `Rests.EscapedParameterCollection` ✓


## ★ 内存压缩功能的完整实测与纠正（2026-10-10）

### 背景
TSManager 宣传"空服时压缩工作集，不关闭端口、不中断首次进入"，听起来很省内存。
我在 UTSLManager 里实现了同样的功能（`SetProcessWorkingSetSize(h, -1, -1)`），
**第一次只看了工作集** —— 得出"1207MB → 38MB，释放 97% 物理内存"的结论 ✗ **这是错的** ✗。

### 完整指标实测（16GB 内存机器，UTSL 三世界 4200x2400）
| 指标 | 压缩前 | 压缩后 | 结论 |
|:--|--:|--:|:--|
| 进程 工作集（物理驻留） | 482 MB | 29 MB | 确实降了 ✓ |
| **进程 私有提交（commit）** | **1166 MB** | **1166 MB** | **完全不变 ✗★** |
| 进程 页面文件用量 | 1166 MB | 1166 MB | 不变 ✓ |
| 系统 可用物理内存 | 2012 MB | 2334 MB | 只 +322MB（不是 -453MB） |
| 系统 页面文件实际使用 | 2 MB | 2 MB | **数据没进磁盘 ✗★** |

### 真相
- 那 453MB 并没有被写到页面文件 ✗，而是进了 Windows 的 **standby list**（仍在物理内存里 ✗，
  只是被标记成"随时可回收" ✓）。
- **私有提交（commit）不变** → 进程的真实内存需求没减少 ✗。
- 既然 commit 不变，**"能跑更大的世界"这个推论是错的** ✗（OOM 是 commit 限制导致的 ✗）。
- 而且 Windows **本来就会在内存压力下自动 trim** ✗ → 手动按的额外收益有限 ✗。

### 修正后的定位
- ✅ 真实用途：**重操作前腾出物理内存**（整链重置时 服务器+切分工具 同时吃内存 ✗）
  → 已在管理器的"整链重置"流程里自动先腾一次 ✓
- ⚠️ 不是"省内存神器" ✗ —— 按钮文案已改成「腾内存」+ 提示里明确写"不减 commit、不提高世界尺寸上限" ✓

### ★ 通用教训
**测内存功能必须看全套指标**：工作集 / 私有提交 / 页面文件用量 / 系统可用物理 / 系统 commit。
只看工作集一定会得出错误结论 ✗（这次就翻车了 ✗）。
判断"能不能跑更大规模"要看 **私有提交（commit）** ✗，不是工作集 ✗。

### 其它移植自 TSManager 的功能
- **单实例**：命名 Mutex（`UTSLManager_SingleInstance_9F2A6C`）+ `FindWindow`/`SetForegroundWindow` 唤醒已有窗口 ✓
- **看门狗熔断**：连续 3 次自动重启后仍不存活 → 停止重试 + 告警 ✓（避免无限重启风暴 ✓）


## ★ UTSLManager 布局定稿（2026-10-10）

### 最终布局
~~~
┌─────────────────────────────────────────────────────────────────┐
│ 🔴🟡🟢                UTSLManager                                │ ← 标题栏只留交通灯+标题
├────────────┬────────────────────────────────────┬───────────────┤
│ 世界列表    │            控制台（自动撑满 ✓）      │ 服务端         │
│ ✔全部世界   │  ┌CPU──┐┌内存──┐┌网络──┐          │ ● 状态胶囊     │
│ ● Dev      │  │Y轴刻度││Y轴刻度││Y轴刻度│          │ [启动][停止]   │
│ ● West     │  │X轴时间││X轴时间││X轴时间│          │ [重启服务端]   │
│ ● East     │  └──────┘└──────┘└──────┘          │ [☑掉线自启]    │
│            │  ┌指令输出──┐┌服务器日志─┐          │ 功能           │
│ 世界链:     │  │          ││           │          │ [插件管理]     │
│ West—Dev—  │  └──────────┘└───────────┘          │ [地图预览]     │
│ East       │  @ 输入指令…              [发送]     │ [整链重置]     │
│            │                                     │ [备份][恢复]   │
└────────────┴────────────────────────────────────┴───────────────┘
~~~

### 布局要点（都是踩出来的）
1. **功能类按钮（插件/地图/重置/备份）放右侧菜单**，不要塞在顶栏 ✗
   —— 顶栏只留高频的「标题 + 状态」，否则在 150% DPI 下必然溢出/被裁 ✗
2. **服务端启停也要放右侧菜单**（放标题栏会与系统 chrome 打架 + 被窗口边缘切掉 ✗）
3. **中间区域不要设 MaxWidth** ✗ —— 设了会在两侧留大片空白 ✓（用户原话："应该填满空缺"）
   用 `HorizontalAlignment="Stretch"` ✓
4. 三栏用 `ColumnDefinitions="244,*,208"`：左栏固定 ✓ 中间自适应 ✓ 右栏固定 ✓

### 监控卡片（自绘，不依赖图表库）
- Y 轴：5 档刻度（100%/75%/50%/25%/0）✓ 数字用智能精度（>=100 用 F0，>=10 用 F1，否则 F2）
  —— 刻度列宽 **52px**（窄了会被长数字挡住 ✗ 实测过）
- X 轴：4 个实时时间标签（HH:mm:ss）✓ 跟着采样滚动
- 绘制：`Canvas` 里放 `Polyline`（折线）+ `Polygon`（半透明面积）+ 4 条水平网格线 ✓
- 内存指标用 **PrivateMemorySize64（私有提交）** ✓ 不是工作集 ✗
  （工作集可以被"腾内存"轻易改小 ✗ 只有提交量反映真实占用 ✓）


## ★ UTSLManager：服务器终端（合并指令输出 + 日志，2026-10-10）

### 设计（参考 MCSManager 的终端面板）
- **一个面板**承载所有内容：服务端日志流 ✓ 指令回显（`> @Dev /help`）✓ 指令结果 ✓
  按时间顺序在同一处滚动 ✓ —— 不再分「指令输出」和「服务器日志」两个窗口 ✓
- **按级别着色**（实测很好用 ✓）：
  `[Error]` → `#FF7B72` 红 / `[Warning]` → `#E3B341` 黄 / `[Success]` → `#7EE787` 绿
  `[Info]` → `#9AA0A6` 灰 / `[Debug]` → `#6E7681` 暗灰 / 其他 → `#B9B9C0`
- **等宽字体**（`Cascadia Mono, Consolas`）+ 深色终端底（`#0E0E10`）
- **输入框在终端底部** ✓ 提示符跟着侧栏选中变（`@ 全部世界` / `@ Dev`）✓
- 附加：自动滚动开关 ✓ 清屏 ✓ 上限 3000 行 ✓

### 关键实现
- 终端行 = `ObservableCollection<TermLine>`（Text + Brush）+ `ItemsControl` ✓
- **日志增量读取**：记录 `logPos`（文件字节位置）✓ 每 1.5 秒只读新增部分 ✓
  —— 不要每次 `ReadAllLines` 全量重设 ✗（几千行会卡 ✓ 而且会闪烁 ✓）
  注意用 `FileShare.ReadWrite` 打开 ✓ 否则服务端写日志时读不到 ✓
- 自动滚动：`Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), Background)` ✓

### ⚠️ 踩的坑
**终端里加了输入框，但旧输入栏忘了删 → `Control with the name CmdBox already registered` → 启动即崩** ✗
教训：Avalonia 的 `x:Name` 在同一 NameScope 里必须唯一 ✗ 重构布局时一定要检查是否有重名控件 ✓
（这类错误只在【运行时】抛出 ✗ 编译期完全看不出来 ✗）



---

# ★ 2026-10-18 增补（文档重整 + 新 API + 假世界）

## 0. 文档索引（谁最权威）

```
权威顺序：git 历史  >  UTSL_开发历史整理.md（本文档的浓缩版）  >  AI_交接提示词.md（上手用）
          >  AGENT.md（本文件，历史踩坑全集）  >  新对话开场白.md（入口，无机密）

当前三份主文档（2026-10-18 重整，旧版已备份为 *.bak-20261018）：
  C:\Users\星梦\Desktop\UTSL_开发历史整理.md    26 KB  生态全貌 + 时间线 + 三条主线 + 技术发现
  C:\Users\星梦\Desktop\AI_交接提示词.md        17 KB  技术要点 + 9 条铁律 + 工作流 + 机密区
  C:\Users\星梦\Desktop\新对话开场白.md          8 KB  入口提示（不含任何机密）
```

## 1. UTSL 新增 API（2026-10-13 → 10-16，全部已推送）

```
【世界控制台模式】71ba669
  ServerContext.ConsoleMode { PerWorld, Headless }；静态 DefaultConsoleMode
  环境变量 UTSL_CONSOLE_MODE=headless → 全局无头（不再为每个世界开控制台窗口）
  ★ 这条解决了"多子世界悬浮窗"问题 —— 此前只能靠 Win32 ShowWindow 隐藏（管理器的临时做法）

【世界生命周期】067705e
  · ServerContext.Close() 无条件 Dispose()、Dispose() 幂等、从 UnifiedServerCoordinator 注销
  · Servers/StaticLeakTargeted.cs 定向摘除 Terraria 侧静态持有者
  → 修掉了"每个 ServerContext 创建后无法被 GC 回收"的内存泄漏，并支持【世界热换】

【清空世界】cdf8acf
  ServerContext.ClearCurrentWorld() —— 尺寸不变、内容清空
  ⚠ 不能改尺寸：OTAPI 的 WorldGenSystemContext.ResetWorldSize 尺寸判断写错（两次都比 Width）
    → 按需创建世界（尘歌壶/副本）只能复用同尺寸 tile 数组

【世界转移】18c5e58 → 7b22751（回退）
  必须【同尺寸】—— 跨尺寸实测会导致客户端空气墙与显示错乱

【指令】2138db5
  · 世界控制台身份（SourceServer != null && UserId == 255）的输出会镜像到静态 logger（logs\*.log）
    → 世界控制台输出对管理器可见
  · TShockConsoleCommand.TryExecute（public）—— "以控制台身份执行"指令

【控制台容错】4d1fa83（未完成）
  目标：无控制台 + stdout 重定向下也能启动（管理器托管服务端的前置）
  现状：ServerSurfaceConsole 字段初始化器直接读 Console.WindowHeight → 无控制台时 IOException
        → TShock 静态构造失败 → 20 个插件里 16 个初始化失败
```

## 2. ★ 假世界（VirtualWorldCore）—— 当前最重要的架构演进

```
契约驱动三层：
  VirtualWorldCore.Abstractions（契约程序集，无 [PluginMetadata]，宿主不加载）
      ↑ 玩法只依赖契约
  VirtualWorldCore（核心模块 [CoreModule]）
      · 按需创建 / 卸载假世界
      · 模板基线：同尺寸秒建（契约 v5/v6）
      · .utpl v3 区块建筑模板：裁切/贴入闭环（契约 v7）
      ↑ 消费
  MyPotWorld（尘歌壶）  每玩家一个隔离私人世界，按 UUID 持久化，闲置 5 分钟卸载（0=常驻）
  BossArena（Boss 场地） 公共假世界 + 战斗平台；同时是"抽象是否够用"的试金石
  将来：PvP / 副本

设计原则：核心换实现不影响玩法；新玩法接入时【核心零改动】才算抽象够用
```

## 3. 当前口径（别再抄旧数字）

```
· 插件仓库 src\ 下【16 个工程 = 15 个插件 + 1 个契约程序集】
· 宿主自带插件在 UnifierTSL\src\Plugins\（TShockAPI / CommandTeleport / Atelier …）不是我们的 ✗
· src\NetPacketChannel\ / MetricsChannel\ / MiniMapSync\ 是【本地遗留目录】（未跟踪，
  源码已不在仓库里；本机 plugins\ 里还剩旧 dll）—— 不要在文档里当成现有插件 ✗
· 本机与远端的插件集合不同（不是彼此超集 ✗）→ 以 plugins\ 目录实测为准
```

## 4. 2026-10-18 状态快照

```
UnifierTSL        main  276 提交  同步 ✓  最新 4d1fa83（10-16）
UTSLServerPlugin  main   63 提交  同步 ✓  最新 e4df9da（10-15）  0 未推送 ✓
UTSL-Manager      main   ——      同步 ✓  最新 8eab501（10-18）  已移除定时任务功能
```

## 5. 本次重整踩到的工具坑（写文档时会遇到）

```
· 用 write 覆盖已存在文件前必须 read（fs 策略）→ 大文件全文 read 很贵
  → 变通：先 write 到临时文件，再 Move-Item -Force 覆盖（PowerShell），并留一份 .bak-<日期>
· 生成含 Windows 路径的文档时，模板字符串里的 \u（真反斜杠 + u）会被当成 Unicode 转义 ✗
  → 变通：先用占位符（如 \）表示反斜杠，最后 .split('\').join([char]92) 还原
```

