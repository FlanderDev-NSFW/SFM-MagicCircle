using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using MagicCircle.Logic;
using RestraintHub;

namespace MagicCircle
{
    /// <summary>
    /// Magic circles that form around her wrists and ankles (or just before her elbows and knees, the limbs past them hidden as
    /// if through a portal). A wearable of RestraintHub: + / - step through its stages (circles only, floating), a tap of F
    /// switches where the circles form, holding F struggles free.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Hub.Guid, "^2.0.0")]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "FlanderDev.SFM.MagicCircle";
        public const string PluginName = "MagicCircle";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Logger;
        internal static Wearable Entry;

        public static ConfigEntry<string> RingLabel, Color;
        public static ConfigEntry<Placement> Where;
        public static ConfigEntry<float> AttemptSeconds, SuccessChance;
        public static ConfigEntry<float> Room, SpinSpeed, Glow, PulseSeconds, PortalBefore;
        public static ConfigEntry<float> FloatHeight, ArmAngle, LegAngle, BobAmplitude, BlendSeconds;
        public static ConfigEntry<bool> HideLimbs, Interior;
        public static ConfigEntry<int> DebugDisable;

        public override void Load()
        {
            Logger = Log;
            RingLabel = Config.Bind("General", "RingLabel", "Magic Circle", "Name in the restraint panel and the worn list.");
            Where = Config.Bind("General", "Placement", Placement.WristsAndAnkles,
                "Where the circles form: WristsAndAnkles, or ElbowsAndKnees (her forearms and lower legs are hidden past them, like a " +
                "portal). A tap of F switches it while the circle is selected.");
            AttemptSeconds = Config.Bind("Unlock", "AttemptSeconds", 4.5f, new ConfigDescription(
                "Seconds of holding F per attempt to struggle free.", new AcceptableValueRange<float>(0.5f, 30f)));
            SuccessChance = Config.Bind("Unlock", "SuccessChance", 0.3f, new ConfigDescription(
                "Chance that an attempt frees her (failures add 0.02, like the game's handcuffs; 1 = the first attempt).",
                new AcceptableValueRange<float>(0.01f, 1f)));
            Color = Config.Bind("Look", "Color", "FF70E0", "The circles' colour as hex RGB (FF70E0 = pink-violet, 70C8FF = icy blue).");
            Room = Config.Bind("Look", "Size", 1.35f, new ConfigDescription(
                "How much wider the circle's empty middle is than her limb.", new AcceptableValueRange<float>(1.05f, 3f)));
            SpinSpeed = Config.Bind("Look", "SpinSpeed", 18f, new ConfigDescription(
                "How fast the circles turn, degrees per second (negative: the other way).", new AcceptableValueRange<float>(-180f, 180f)));
            Glow = Config.Bind("Look", "Glow", 0.6f, new ConfigDescription(
                "Strength of the soft glow around the circles (0 = none).", new AcceptableValueRange<float>(0f, 1f)));
            PulseSeconds = Config.Bind("Look", "PulseSeconds", 2.6f, new ConfigDescription(
                "Length of one slow pulse of the glow.", new AcceptableValueRange<float>(0.5f, 10f)));
            PortalBefore = Config.Bind("Look", "PortalBeforeCm", 4f, new ConfigDescription(
                "ElbowsAndKnees: how far before the joint the circle forms (cm); the hidden limb ends there.", new AcceptableValueRange<float>(0f, 15f)));
            HideLimbs = Config.Bind("Look", "HideLimbs", true,
                "ElbowsAndKnees: hide her forearms and lower legs past the circles (shrunk into the joint).");
            Interior = Config.Bind("Look", "PortalInterior", true,
                "ElbowsAndKnees with hidden limbs: from her body's side one can look into the portals and see her limbs go on inside " +
                "(a tunnel behind each circle, invisible from every other side). Off: a flat glowing surface closes each circle.");
            FloatHeight = Config.Bind("Float", "HeightCm", 20f, new ConfigDescription(
                "Floating stage: how high her feet float above the ground (cm).", new AcceptableValueRange<float>(0f, 100f)));
            ArmAngle = Config.Bind("Float", "ArmAngle", 25f, new ConfigDescription(
                "Floating stage: each arm's angle out from straight up (degrees; a narrow X).", new AcceptableValueRange<float>(0f, 80f)));
            LegAngle = Config.Bind("Float", "LegAngle", 8f, new ConfigDescription(
                "Floating stage: each leg's angle out from straight down (degrees).", new AcceptableValueRange<float>(0f, 40f)));
            BobAmplitude = Config.Bind("Float", "BobCm", 1.2f, new ConfigDescription(
                "Floating stage: how far she drifts slowly up and down (cm).", new AcceptableValueRange<float>(0f, 10f)));
            BlendSeconds = Config.Bind("Float", "BlendSeconds", 1.2f, new ConfigDescription(
                "How long she takes to rise into / sink out of the floating pose.", new AcceptableValueRange<float>(0.1f, 5f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0, "Bit mask for tests: 1 = do not pose her; 2 = do not hide limbs.");

            ClassInjector.RegisterTypeInIl2Cpp<CircleController>();
            AddComponent<CircleController>();

            Entry = new Wearable
            {
                Id = "MagicCircle",
                Label = () => RingLabel.Value,
                OnSave = Patches.Save,
                OnLoad = Patches.Load,
                OnNewGame = () => CircleController.Instance?.Release("new game"),
                Holds = BodyParts.None, // set by the stage: the floating stage holds her arms, legs and place
                IsWorn = () => CircleController.Instance != null && CircleController.Instance.Machine.Worn,
                Blocks = () => CircleController.Instance == null ? RestraintHub.Blocks.None : CircleController.Instance.Blocks(),
                PutOn = () => CircleController.Instance == null ? "not ready" : CircleController.Instance.PutOn(),
                TakeOff = () => CircleController.Instance?.Release("taken off"),
                Attempt = () => CircleController.Instance == null ? 0f : CircleController.Instance.Machine.Attempt,
                OnTap = () => CircleController.Instance?.SwitchPlacement(),
                Controls = "+/- stage, tap F: place",
                Settings = new[]
                {
                    new RestraintSetting
                    {
                        Name = "stage", Unit = "0 circles only, 1 floating",
                        Get = () => CircleController.Instance == null ? 0f : CircleController.Instance.Stage,
                        Set = v => CircleController.Instance == null ? "not ready" : CircleController.Instance.SetStage((int)System.Math.Round(v), false),
                    },
                    new RestraintSetting
                    {
                        Name = "placement", Unit = "0 wrists and ankles, 1 elbows and knees",
                        Get = () => (float)CircleController.CurrentPlacement,
                        Set = v => CircleController.SetPlacement(v >= 0.5f ? Placement.ElbowsAndKnees : Placement.WristsAndAnkles, false),
                    },
                },
            };
            Hub.Register(Entry);
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready");
        }
    }
}
