using ECommons.DalamudServices;
using ECommons.EzIpcManager;
using System.Globalization;

namespace BFE.IPC
{
    public class BossModIPC
    {
        public const string Name = "BossMod";
        public const string Repo = "https://github.com/awgil/ffxiv_bossmod";
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public BossModIPC() => EzIPC.Init(this, Name, SafeWrapper.AnyException);
        public bool Installed => P.pluginDependencies.IsBossModFamilyLoaded;

        [EzIPC] public readonly Func<uint, bool> HasModuleByDataId;
        [EzIPC] public readonly Func<IReadOnlyList<string>, bool, List<string>> Configuration;
        [EzIPC("Presets.Get", true)] public readonly Func<string, string?> Presets_Get;
        [EzIPC("Presets.Create", true)] public readonly Func<string, bool, bool> Presets_Create;
        [EzIPC("Presets.Delete", true)] public readonly Func<string, bool> Presets_Delete;
        [EzIPC("Presets.GetActive", true)] public readonly Func<string> Presets_GetActive;
        [EzIPC("Presets.SetActive", true)] public readonly Func<string, bool> Presets_SetActive;
        [EzIPC("Presets.ClearActive", true)] public readonly Func<bool> Presets_ClearActive;
        [EzIPC("Presets.GetForceDisabled", true)] public readonly Func<bool> Presets_GetForceDisabled;
        [EzIPC("Presets.SetForceDisabled", true)] public readonly Func<bool> Presets_SetForceDisabled;
        /** string presetName, string moduleTypeName, string trackName, string value*/
        [EzIPC("Presets.AddTransientStrategy")] public readonly Func<string, string, string, string, bool> Presets_AddTransientStrategy;

        public void AddPreset(string name, string preset)
        {
            if (!Installed || Presets_Get == null || Presets_Create == null) return;

            try
            {
                //check if our preset does not exist
                if (Presets_Get(name) == null)
                    //load it
                    Svc.Log.Debug($"RoR Preset Loaded: {Presets_Create(preset, true)}");
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"BossMod IPC AddPreset failed: {ex.Message}");
            }
        }

        public void RefreshPreset(string name, string preset)
        {
            if (!Installed || Presets_Get == null || Presets_Delete == null) return;

            try
            {
                if (Presets_Get(name) != null)
                    Presets_Delete(name);
                AddPreset(name, preset);
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"BossMod IPC RefreshPreset failed: {ex.Message}");
            }
        }

        public void SetPreset(string name)
        {
            if (!Installed || Presets_GetActive == null || Presets_SetActive == null) return;

            try
            {
                if (Presets_GetActive() != name)
                {
                    Presets_SetActive(name);
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"BossMod IPC SetPreset failed: {ex.Message}");
            }
        }

        public void DisablePresets()
        {
            if (!Installed || Presets_GetForceDisabled == null || Presets_SetForceDisabled == null) return;

            try
            {
                if (!Presets_GetForceDisabled())
                    Presets_SetForceDisabled();
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"BossMod IPC DisablePresets failed: {ex.Message}");
            }
        }

        public void SetRange(float range)
        {
            if (!Installed || Presets_AddTransientStrategy == null) return;

            try
            {
                Presets_AddTransientStrategy("RoR Boss", "BossMod.Autorotation.MiscAI.StayCloseToTarget", "range", MathF.Round(range, 1).ToString(CultureInfo.InvariantCulture));
                Presets_AddTransientStrategy("ROR Passive", "BossMod.Autorotation.MiscAI.StayCloseToTarget", "range", MathF.Round(range, 1).ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"BossMod IPC SetRange failed: {ex.Message}");
            }
        }
    }
}
