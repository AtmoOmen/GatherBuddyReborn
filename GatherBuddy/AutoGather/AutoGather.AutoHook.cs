using GatherBuddy.AutoGather.Lists;
using GatherBuddy.AutoHookIntegration;
using GatherBuddy.Classes;
using GatherBuddy.Plugin;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace GatherBuddy.AutoGather;

public partial class AutoGather
{
    private const string AutoHookGlobalPresetSelectionSentinel = "__GBR_USE_AUTOHOOK_GLOBAL_PRESET__";
    private const string AutoHookGlobalPresetDisplayName = "Global Preset";
    private GatherTarget? _currentAutoHookTarget;
    private string? _currentAutoHookPresetName;
    private string? _currentAutoHookTargetPresetName;
    private bool _isCurrentPresetUserOwned;
    private bool _isUsingAutoHookGlobalPreset;
    private bool? _autoHookPluginStateBeforeAutoGig;

    private void CleanupAutoHookIfNeeded(GatherTarget newTarget)
    {
        if (!GatherBuddy.Config.AutoGatherConfig.UseAutoHook)
        {
            return;
        }

        if (_currentAutoHookTarget == null || _currentAutoHookPresetName == null)
        {
            return;
        }

        if (_currentAutoHookTarget.Value.FishingSpot?.Id != newTarget.FishingSpot?.Id)
        {
            CleanupAutoHook();
        }
    }

    private void SetupAutoHookForFishing(GatherTarget target)
    {
        if (!GatherBuddy.Config.AutoGatherConfig.UseAutoHook)
        {
            return;
        }

        if (!AutoHook.Enabled)
        {
            GatherBuddy.Log.Debug("[AutoGather] AutoHook 不可用，跳过预设生成");
            return;
        }

        if (target.Fish == null)
        {
            return;
        }

        CleanupAutoHookIfNeeded(target);

        var shouldUseGlobalAutoHookPreset = GatherBuddy.Config.AutoGatherConfig.UseAutoHookGlobalPreset && !target.Fish.IsSpearFish;
        if (_currentAutoHookTarget?.Fish?.ItemId == target.Fish.ItemId
            && _currentAutoHookPresetName != null
            && _isUsingAutoHookGlobalPreset != shouldUseGlobalAutoHookPreset)
        {
            GatherBuddy.Log.Debug("[AutoGather] AutoHook preset mode changed, resetting current AutoHook state.");
            CleanupAutoHook();
        }

        if (_currentAutoHookTarget?.Fish?.ItemId == target.Fish.ItemId
            && _currentAutoHookPresetName != null
            && _isUsingAutoHookGlobalPreset == shouldUseGlobalAutoHookPreset)
        {
            if (_isUsingAutoHookGlobalPreset)
            {
                AutoHook.SetPreset?.Invoke(AutoHookGlobalPresetSelectionSentinel);
            }

            if (target.Fish.IsSpearFish)
            {
                if (!EnableAutoGig())
                {
                    return;
                }
            }
            else
            {
                AutoHook.SetPluginState?.Invoke(true);
                AutoHook.SetAutoStartFishing?.Invoke(true);
            }
            GatherBuddy.Log.Verbose(
                $"[AutoGather] 重新启用现有 AutoHook 预设 '{(_isUsingAutoHookGlobalPreset ? AutoHookGlobalPresetDisplayName : _currentAutoHookPresetName)}'");
            return;
        }

        try
        {
            if (shouldUseGlobalAutoHookPreset)
            {
                GatherBuddy.Log.Information(
                    $"[AutoGather] Using AutoHook global preset for {target.Fish.Name[GatherBuddy.Language]}.");
                AutoHook.SetPreset?.Invoke(AutoHookGlobalPresetSelectionSentinel);

                _currentAutoHookTarget = target;
                _currentAutoHookPresetName = AutoHookGlobalPresetDisplayName;
                _currentAutoHookTargetPresetName = null;
                _isCurrentPresetUserOwned = false;
                _isUsingAutoHookGlobalPreset = true;

                if (AutoHook.SetPluginState == null)
                {
                    GatherBuddy.Log.Error("[AutoGather] SetPluginState IPC 为空");
                    return;
                }

                AutoHook.SetPluginState.Invoke(true);
                AutoHook.SetAutoStartFishing?.Invoke(true);
                _autoHookSetupComplete = true;
                GatherBuddy.Log.Information("[AutoGather] AutoHook enabled with global preset for fishing");
                return;
            }
            // Check if the target fish is an intuition fish
            var isIntuitionFish = target.Fish.Predators.Length > 0 && Array.TrueForAll(target.Fish.Predators, p => !p.Item1.IsSpearFish);

            // For intuition fish, always use target fish (two-preset system handles predators)
            // For non-intuition fish with predators, check if we should use predator instead
            var presetFish = target.Fish;
            if (!isIntuitionFish && target.Fish.Predators.Length > 0)
            {
                var firstPredator = target.Fish.Predators[0].Item1;
                if (target.FishingSpot?.IsShadowNode != true)
                {
                    presetFish = firstPredator;
                    GatherBuddy.Log.Debug($"[AutoGather] 目标鱼 {target.Fish.Name[GatherBuddy.Language]} 位于父节点，使用前置鱼 {presetFish.Name[GatherBuddy.Language]} 作为预设");
                }
                else
                {
                    GatherBuddy.Log.Debug($"[AutoGather] 目标鱼 {target.Fish.Name[GatherBuddy.Language]} 位于影子节点，使用目标鱼作为预设");
                }
            }

            var fishName = presetFish.Name[GatherBuddy.Language];
            var fishId = presetFish.ItemId;
            string? presetName = null;
            var isUserPreset = false;

            if (GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets)
            {
                string? userPresetName = null;
                if (isIntuitionFish)
                {
                    // For intuition fish, look for presets named after target fish ID
                    var targetFishId = target.Fish.ItemId;
                    userPresetName = FindAutoHookPreset($"{targetFishId}_Predators");
                    userPresetName ??= FindAutoHookPreset(targetFishId.ToString());
                }
                else
                {
                    userPresetName = FindAutoHookPreset(fishId.ToString());
                }

                if (userPresetName != null)
                {
                    presetName = userPresetName;
                    isUserPreset = true;

                    GatherBuddy.Log.Information($"[AutoGather] 为 {fishName} 找到用户预设 '{presetName}'");
                    AutoHook.SetPreset?.Invoke(presetName);
                }
                else
                {
                    GatherBuddy.Log.Debug($"[AutoGather] 未找到鱼类 ID {fishId} 的用户预设，将自动生成");
                }
            }

            if (presetName == null)
            {
                // Build fish list: if at shadow node with multiple predators, include them all
                var fishList = new[] { presetFish };
                if (presetFish == target.Fish && target.Fish.Predators.Length > 1 && target.FishingSpot?.IsShadowNode == true)
                {
                    // At shadow node targeting fish with multiple predators - include all predators (skip first) + target
                    var predators = target.Fish.Predators;
                    fishList = new Fish[predators.Length];
                    for (var i = 1; i < predators.Length; ++i)
                    {
                        fishList[i - 1] = predators[i].Item1;
                    }

                    fishList[^1] = target.Fish;
                    GatherBuddy.Log.Debug($"[AutoGather] 为多头捕食者链构建包含 {fishList.Length} 条鱼的预设: {string.Join(", ", Array.ConvertAll(fishList, f => f.Name[GatherBuddy.Language]))}");
                }

                presetName = $"GBR_{fishName.Replace(" ", "")}_{DateTime.Now:HHmmss}";

                // For intuition fish generator will generate _Predators and _Target presets
                if (isIntuitionFish)
                {
                    GatherBuddy.Log.Information($"[AutoGather] 为 {fishName} 创建直感预设");
                    presetName += "_Predators";
                }
                else
                {
                    GatherBuddy.Log.Information($"[AutoGather] 为 {fishName} 创建 AutoHook 预设 '{presetName}'");
                }

                bool success;
                if (presetFish.IsSpearFish)
                {
                    success = AutoHookService.ExportSpearfishingPresetToAutoHook(presetName.Replace("_Predators", ""), fishList);
                }
                else
                {
                    var gbrPreset = MatchConfigPreset(presetFish);
                    success = AutoHookService.ExportPresetToAutoHook(presetName.Replace("_Predators", ""), fishList, gbrPreset, selectPreset: true);
                }

                if (!success)
                {
                    GatherBuddy.Log.Error($"[AutoGather] 创建 AutoHook 预设失败");
                    return;
                }
            }

            _currentAutoHookTarget = target;
            _currentAutoHookPresetName = presetName;
            _isCurrentPresetUserOwned = isUserPreset;
            _isUsingAutoHookGlobalPreset = false;

            if (isIntuitionFish && !isUserPreset)
            {
                var baseName = presetName.Replace("_Predators", "");
                _currentAutoHookTargetPresetName = baseName + "_Target";
            }
            else
            {
                _currentAutoHookTargetPresetName = null;
            }

            if (target.Fish.IsSpearFish)
            {
                if (EnableAutoGig())
                {
                    GatherBuddy.Log.Information("[AutoGather] 已为刺鱼开启 AutoHook 和 AutoGig");
                }
            }
            else
            {
                if (AutoHook.SetPluginState == null)
                {
                    GatherBuddy.Log.Error("[AutoGather] SetPluginState IPC 为空");
                }
                else
                {
                    AutoHook.SetPluginState.Invoke(true);
                    AutoHook.SetAutoStartFishing?.Invoke(true);
                    _autoHookSetupComplete = true;
                    GatherBuddy.Log.Information("[AutoGather] AutoHook 已为钓鱼启用");
                }
            }

            var presetType = isUserPreset ? "user" : "generated";
            GatherBuddy.Log.Information($"[AutoGather] 为 {fishName} 的 AutoHook 预设 '{presetName}'（{presetType}）已成功选择并激活");
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Error($"[AutoGather] 设置 AutoHook 时发生异常: {ex.Message}");
        }
    }

    private bool EnableAutoGig()
    {
        if (AutoHook.SetPluginState == null)
        {
            GatherBuddy.Log.Error("[AutoGather] SetPluginState IPC 为空");
            return false;
        }

        if (AutoHook.SetAutoGigState == null)
        {
            GatherBuddy.Log.Error("[AutoGather] SetAutoGigState IPC 为空");
            return false;
        }

        _autoHookPluginStateBeforeAutoGig ??= AutoHook.GetPluginState?.Invoke() ?? false;
        AutoHook.SetPluginState.Invoke(true);
        AutoHook.SetAutoGigState.Invoke(true);
        _autoHookSetupComplete = true;
        return true;
    }

    private void CleanupAutoHook()
    {
        if (!AutoHook.Enabled)
        {
            return;
        }

        try
        {
            if (_currentAutoHookPresetName != null)
            {
                if (_isUsingAutoHookGlobalPreset)
                {
                    GatherBuddy.Log.Debug("[AutoGather] Leaving AutoHook on its global preset.");
                }
                else if (_isCurrentPresetUserOwned)
                {
                    GatherBuddy.Log.Debug($"[AutoGather] 保留用户拥有的预设 '{_currentAutoHookPresetName}'");
                }
                else
                {
                    if (_currentAutoHookTarget.HasValue && _currentAutoHookTarget.Value.Fish?.IsSpearFish == true)
                    {
                        GatherBuddy.Log.Warning(
                            $"[AutoGather] 生成的 AutoGig 预设 '{_currentAutoHookPresetName}' 仍保留在 AutoHook 中，因为 AutoHook 未提供通过 IPC 删除 AutoGig 预设的接口；请在 AutoHook 的 AutoGig 预设中手动删除。");
                    }
                    else
                    {
                        AutoHook.SetPreset?.Invoke(_currentAutoHookPresetName);
                        AutoHook.DeleteSelectedPreset?.Invoke();
                        GatherBuddy.Log.Debug($"[AutoGather] 已删除 GBR 生成的预设 '{_currentAutoHookPresetName}'");

                        if (_currentAutoHookTargetPresetName != null)
                        {
                            AutoHook.SetPreset?.Invoke(_currentAutoHookTargetPresetName);
                            AutoHook.DeleteSelectedPreset?.Invoke();
                            GatherBuddy.Log.Debug($"[AutoGather] 已删除 GBR 生成的预设 '{_currentAutoHookTargetPresetName}'");
                        }
                    }
                }
            }

            AutoHook.SetAutoStartFishing?.Invoke(false);
            AutoHook.SetAutoGigState?.Invoke(false);
            AutoHook.SetPluginState?.Invoke(_autoHookPluginStateBeforeAutoGig ?? false);
            GatherBuddy.Log.Debug("[AutoGather] AutoHook/AutoGig 清理完成");

            _currentAutoHookTarget = null;
            _currentAutoHookPresetName = null;
            _currentAutoHookTargetPresetName = null;
            _isCurrentPresetUserOwned = false;
            _isUsingAutoHookGlobalPreset = false;
            _autoHookPluginStateBeforeAutoGig = null;
            _autoHookSetupComplete = false;
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Error($"[AutoGather] 清理 AutoHook 时发生异常: {ex.Message}");
        }
    }

    private static string? FindAutoHookPreset(string fishId)
    {
        try
        {
            // Resolve AutoHook config path: .../pluginConfigs/AutoHook.json
            var pluginConfigsDir = Dalamud.PluginInterface.ConfigDirectory.Parent?.FullName;
            if (string.IsNullOrEmpty(pluginConfigsDir))
            {
                return null;
            }

            var configPath = Path.Combine(pluginConfigsDir, "AutoHook.json");
            if (!File.Exists(configPath))
            {
                GatherBuddy.Log.Debug($"[AutoGather] 在 {configPath} 找不到 AutoHook 配置");
                return null;
            }

            var json = File.ReadAllText(configPath);
            var config = JObject.Parse(json);

            if (config["HookPresets"]?["CustomPresets"] is not JArray customPresets)
            {
                GatherBuddy.Log.Debug("[AutoGather] 在 AutoHook 配置中找不到 CustomPresets");
                return null;
            }

            foreach (var preset in customPresets)
            {
                var presetName = preset?["PresetName"]?.ToString();
                if (presetName != null && presetName.Equals(fishId, StringComparison.Ordinal))
                {
                    GatherBuddy.Log.Debug($"[AutoGather] 在配置中找到匹配的预设: {presetName}");
                    return presetName;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Error($"[AutoGather] 读取 AutoHook 配置时出错: {ex.Message}");
            return null;
        }
    }
}
