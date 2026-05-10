# SCP:SL Custom Room Plugin

适用于 SCP: Secret Laboratory 的 EXILED 暖场 SCP 选择插件。

状态：公开草稿版本。

## 中文说明

在等待玩家阶段，插件会把玩家移动到 SCP-173 门附近的选择大厅。玩家通过与硬币互动选择想要的 SCP。回合倒计时和原版职业分配仍由游戏本身处理。

回合开始后，插件只交换原版已经分配出的职业：

- 如果原版生成了玩家选择的 SCP，选择池中的一名玩家可以获得该 SCP 位置。
- 被替换的原 SCP 玩家会获得选择者原本的原版职业。
- 如果原版没有生成该 SCP，选择会被跳过。
- 插件不会额外创建 SCP，也不会随机创建回退职业。

## 构建

本项目会引用 SCP:SL 专用服务器安装目录中的托管程序集。如果服务器文件不在默认位置，请在构建时传入 `ScpSlManagedPath`：

```powershell
dotnet restore
dotnet build -c Release -p:ScpSlManagedPath="C:\path\to\SCPSL_Data\Managed"
```

插件 DLL 输出位置：

```text
src/ScpslCustomRoomPlugin/bin/Release/net48/ScpslCustomRoomPlugin.dll
```

把该 DLL 复制到目标服务器对应端口的 EXILED 插件目录。

## 配置

EXILED 会在对应端口的插件配置目录下生成配置文件，例如：

```text
%AppData%\EXILED\Configs\Plugins\scpsl_custom_room_plugin\7780.yml
```

启用中文游戏内提示：

```yaml
use_chinese_localization: true
```

该开关会把倒计时提示、当前选择提示和初始选择说明切换为简体中文。SCP 标签默认仍为数字编号，可通过 `scp_class_options` 自行配置。

## 项目结构

- `src/ScpslCustomRoomPlugin/Plugin.cs` - 插件入口和事件注册。
- `src/ScpslCustomRoomPlugin/Config.cs` - EXILED 配置对象。
- `src/ScpslCustomRoomPlugin/WarmupSelectionController.cs` - 选择大厅、原版倒计时状态、玩家选择和回合开始后的交换逻辑。

## English

EXILED plugin for SCP: Secret Laboratory warmup SCP class selection.

During waiting-for-players, the plugin moves players into an SCP selector lobby anchored at the SCP-173 gate. Players select a preferred SCP by interacting with a coin. The game still owns the native lobby countdown and the vanilla round role distribution.

After vanilla round start, the plugin only swaps already-assigned vanilla roles:

- If vanilla spawned a selected SCP role, one selected player from that SCP pool can receive that slot.
- The displaced vanilla SCP holder receives the selected player's original vanilla role.
- If vanilla did not spawn that SCP role, the selection is skipped.
- The plugin does not create additional SCP roles or fallback roles.

### Build

This project references SCP:SL managed assemblies from a dedicated server install. If your server files are somewhere else, pass `ScpSlManagedPath` to the build:

```powershell
dotnet restore
dotnet build -c Release -p:ScpSlManagedPath="C:\path\to\SCPSL_Data\Managed"
```

The plugin DLL is emitted under:

```text
src/ScpslCustomRoomPlugin/bin/Release/net48/ScpslCustomRoomPlugin.dll
```

Copy that DLL to the port-specific EXILED plugins folder for the target server.

### Configuration

EXILED generates the config under the port-specific plugin config folder, for example:

```text
%AppData%\EXILED\Configs\Plugins\scpsl_custom_room_plugin\7780.yml
```

Set this option to enable Simplified Chinese player-facing text in-game:

```yaml
use_chinese_localization: true
```

When enabled, countdown hints, selected-SCP status, and the initial selector instruction are shown in Simplified Chinese. The SCP selector labels remain configurable through `scp_class_options`; the default labels are numeric SCP IDs.

## License

MIT. See `LICENSE`.
