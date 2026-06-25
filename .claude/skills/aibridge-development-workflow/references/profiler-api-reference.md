<!-- AIBRIDGE:GENERATED COMMAND REFERENCE - DO NOT EDIT MANUALLY -->
# AIBridge Command Reference

此文件由 AIBridge 自动生成。需要修改命令说明时，请修改对应 ICommand 的 SkillDoc/SkillDescription。
`$CLI` 表示当前平台的 AIBridge CLI 调用方式，Windows 项目通常是 `./.aibridge/cli/AIBridgeCLI.exe`。

### `profiler` - Editor Profiler Diagnostics

Use only for performance/Profiler debugging. The command returns AIBridge JSON snapshots; `save_data` / `load_data` do not read or write Unity native `.data` Profiler files.

```bash
$CLI profiler start
$CLI profiler get_status
$CLI profiler list_modules
$CLI profiler enable_module --module "Memory" --enabled true
$CLI profiler capture_frame
$CLI profiler get_memory_stats
$CLI profiler get_rendering_stats
$CLI profiler get_script_stats
$CLI profiler save_data --path ".aibridge/profiler/latest.json"
$CLI profiler load_data --path ".aibridge/profiler/latest.json"
$CLI profiler stop
```

Unsupported or unstable Unity Profiler surfaces are reported in the `unsupported` array instead of being silently treated as available. Runtime Player performance evidence still uses `runtime perf`.
