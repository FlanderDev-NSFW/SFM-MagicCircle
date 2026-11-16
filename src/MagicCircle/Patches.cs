using System;
using RestraintHub;

namespace MagicCircle
{
    /// <summary>The circle's own save data (through RestraintHub's OnSave / OnLoad): worn, struggle chance, stage.</summary>
    internal static class Patches
    {
        public const string SaveKeyLocked = "MagicCircle.Locked";
        public const string SaveKeyChance = "MagicCircle.Chance";
        public const string SaveKeyStage = "MagicCircle.Stage";

        internal static void Save(SaveSlot slot)
        {
            try
            {
                var c = CircleController.Instance;
                if (c == null) return;
                slot.Save(SaveKeyLocked, c.Machine.Worn);
                slot.Save(SaveKeyChance, (double)c.Machine.Chance);
                slot.Save(SaveKeyStage, c.Machine.Worn ? c.Stage : 0);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: locked=" + c.Machine.Worn + " stage=" + c.Stage + " path=" + slot.Path);
            }
            catch (Exception e) { CircleController.Fail("save hook", e); }
        }

        internal static void Load(SaveSlot slot)
        {
            try
            {
                var c = CircleController.Instance;
                if (c == null) return;
                bool locked = slot.Load(SaveKeyLocked, false);
                double chance = slot.Load(SaveKeyChance, -1.0);
                int stage = slot.Load(SaveKeyStage, 0);
                c.OnSaveLoaded(locked, (float)Math.Max(chance, 0.0), stage);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] load: locked=" + locked + " stage=" + stage + " path=" + slot.Path);
            }
            catch (Exception e) { CircleController.Fail("load hook", e); }
        }
    }
}
