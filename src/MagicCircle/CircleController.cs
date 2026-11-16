using System;
using System.Globalization;
using ExposureUnnoticed2.Master.Action;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.Sound;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using MagicCircle.Logic;
using RestraintHub;
using SfmKit.Game;
using SfmKit.Pure;
using UnityEngine;

namespace MagicCircle
{
    /// <summary>
    /// Owns the magic circle: the lock (struggle with F), the stage (+ / -), where the circles form (tap F), the floating pose
    /// (LateUpdate), the hidden limbs and the visible circles (onPreCull). Bridge diagnostics (session with --allow-invoke):
    /// <c>game call MagicCircle.CircleController PutOnNow</c>, <c>Step 1</c>, <c>Switch</c>, <c>ReleaseNow</c>;
    /// <c>game get MagicCircle.CircleController::Status</c>.
    /// </summary>
    public sealed class CircleController : MonoBehaviour
    {
        internal static CircleController Instance;
        public CircleController(IntPtr pointer) : base(pointer) { }

        internal readonly LockMachine Machine = new LockMachine();

        /// <summary>"floating (w 1.00), wrists and ankles, hidden no, ..." for diagnostics.</summary>
        public static string Status = "off";

        /// <summary>Diagnostics: her lower toe joint's height above her root (the ground she stood on), as drawn (m).</summary>
        public static float ToesUp;

        private static int _failures;
        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake() => Instance = this;

        // --- state ---------------------------------------------------------------------------------------------------------

        internal int Stage { get; private set; }
        /// <summary>0 = the animation, 1 = fully in the stage's pose (blends over Float.BlendSeconds).</summary>
        private float _weight;
        private Vector3 _origin;
        private Quaternion _frame = Quaternion.identity;
        private BodyDims _body;
        private bool _measured;
        private CircleRig _rig;

        /// <summary>A placement set by a mission (RtHub_Set "placement"), used instead of the config until the circle comes off.</summary>
        private static Placement? _missionPlacement;
        internal static Placement CurrentPlacement => _missionPlacement ?? Plugin.Where.Value;

        [HideFromIl2Cpp]
        internal RestraintHub.Blocks Blocks() => Stages.Holds(Stage)
            ? RestraintHub.Blocks.Moving | RestraintHub.Blocks.Crouching | RestraintHub.Blocks.HandActions | RestraintHub.Blocks.PoseSkills
            : RestraintHub.Blocks.None;

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            if (PlayerController.Instance == null || GameState.GameStateData == null) return;
            Machine.AttemptSeconds = Plugin.AttemptSeconds.Value;
            Machine.BaseChance = Plugin.SuccessChance.Value;
            Machine.CloseSeconds = Machine.OpenSeconds = 0.8f; // the circles grow in / fade out
            int step = Plugin.Entry.TakeStep();
            if (step != 0 && Machine.State == LockState.Locked)
            {
                string why = SetStage(Stage + step, true);
                if (why != null) Say(why);
            }
            bool struggle = Plugin.Entry.StruggleHeld && Machine.State == LockState.Locked;
            switch (Machine.Tick(Time.deltaTime, struggle, UnityEngine.Random.value))
            {
                case LockMachine.Event.Locked:
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: circles formed");
                    break;
                case LockMachine.Event.AttemptFailed:
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: failed, chance now " + F(Machine.Chance));
                    break;
                case LockMachine.Event.Unlocking:
                    GameUi.PlaySe(InGameSoundManager.SeType.Unlock);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: success after " + Machine.Failures + " failures");
                    SetStage(Stages.Visual, false); // she sinks down while the circles fade
                    break;
                case LockMachine.Event.Unlocked:
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] release: struggled free");
                    Release("struggled free");
                    break;
            }
            float target = Stages.Holds(Stage) && Machine.Worn ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime / Plugin.BlendSeconds.Value);
            Status = (Machine.Worn ? Machine.State + ", " + Stages.Name(Stage) + " (w " + F(_weight) + "), " + Stages.PlacementName(CurrentPlacement)
                      + ", hidden " + (FloatPoser.IsHidden ? "yes" : "no") : "off") + ", shader " + CircleRig.ShaderName;
        }

        // --- putting on / off ----------------------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        internal string PutOn()
        {
            var player = PlayerController.Instance;
            var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
            if (anim == null || GameState.GameStateData == null) return "not in a stage";
            if (Machine.Worn) return "it is already on";
            if (!FloatPoser.Measure(anim, out _body, out string problem)) return problem;
            _measured = true;
            EnsureRig(anim);
            if (!_rig.Built) return "the circles could not be made (see the log)";
            Stage = Stages.Visual;
            _weight = 0f;
            UpdateHolds();
            Machine.RequestLock();
            GameUi.PlaySe(InGameSoundManager.SeType.CommonEquip);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] put on: circles forming at the " + Stages.PlacementName(CurrentPlacement));
            return null;
        }

        [HideFromIl2Cpp]
        internal void Release(string why)
        {
            bool was = Machine.Worn;
            Machine.Reset();
            Stage = Stages.Visual;
            _weight = 0f;
            _missionPlacement = null;
            UpdateHolds();
            var player = PlayerController.Instance;
            FloatPoser.Unhide(player == null || player.Pca == null ? null : player.Pca.Animator);
            _rig?.SetVisible(false);
            if (was) Plugin.Logger.LogInfo("[SFM-ACCEPT] release: " + why);
        }

        [HideFromIl2Cpp]
        private void EnsureRig(Animator anim)
        {
            if (_rig == null) _rig = new CircleRig();
            if (!_rig.Built) _rig.Build(anim.gameObject.layer);
        }

        [HideFromIl2Cpp]
        internal void OnSaveLoaded(bool locked, float chance, int stage)
        {
            Release("load");
            if (!locked) return;
            Machine.Restore(true, chance);
            _measured = false; // measured in the next LateUpdate, once her body settings apply
            Stage = Stages.Clamp(stage);
            _weight = Stages.Holds(Stage) ? 1f : 0f;
            _frame = Quaternion.identity;
            _needFrame = true;
            UpdateHolds();
        }

        private bool _needFrame;

        // --- stage and placement -------------------------------------------------------------------------------------------

        /// <summary>Go to <paramref name="stage"/>: null when done, else why not. <paramref name="fromKeys"/>: the player's + / -
        /// (messages for the ends of the range).</summary>
        [HideFromIl2Cpp]
        internal string SetStage(int stage, bool fromKeys)
        {
            if (!Machine.Worn) return "the magic circle is not on";
            int want = Stages.Clamp(stage);
            if (want == Stage)
                return !fromKeys ? null : stage > Stage ? "The circles can't hold her any tighter." : "The circles only glow now.";
            if (Stages.Holds(want) && !Stages.Holds(Stage))
            {
                string busy = Busy();
                if (busy != null) return busy;
                var player = PlayerController.Instance;
                if (InPose(player)) player.Pca.PlayerAction.ChangeAction(ActionType.None);
                CaptureFrame(player);
            }
            Stage = want;
            UpdateHolds();
            Plugin.Logger.LogInfo("[SFM-ACCEPT] stage: " + Stage + " " + Stages.Name(Stage) + (fromKeys ? "" : " (mission or struggle)"));
            if (fromKeys) Say(Stage == Stages.Float ? "The circles lift her off the ground." : "The circles let her down and only glow.");
            return null;
        }

        /// <summary>Why the circles can't hold her now (another restraint holds her arms, legs or place; the game's handcuffs),
        /// else null. Only while RestraintHub's blocking is on.</summary>
        [HideFromIl2Cpp]
        private static string Busy()
        {
            try { if (!RestraintHub.Plugin.BlockEachOther.Value) return null; }
            catch (Exception) { /* hub internals changed: check anyway */ }
            const BodyParts need = BodyParts.Arms | BodyParts.Legs | BodyParts.Spot;
            foreach (var w in Hub.Wearables)
            {
                if (w == Plugin.Entry || (w.Holds & need) == 0) continue;
                bool worn;
                try { worn = w.IsWorn != null && w.IsWorn(); }
                catch (Exception) { worn = false; }
                if (!worn) continue;
                string name;
                try { name = w.Label?.Invoke() ?? w.Id; }
                catch (Exception) { name = w.Id; }
                return "The circles can't hold her while the " + name + " is on.";
            }
            if (GameState.GameStateData != null && GameState.GameStateData.PlayerIsAttachHandcuffs)
                return "The circles can't hold her while the handcuffs are on.";
            return null;
        }

        [HideFromIl2Cpp]
        private void UpdateHolds()
        {
            Plugin.Entry.Holds = Machine.Worn && Stages.Holds(Stage) ? BodyParts.Arms | BodyParts.Legs | BodyParts.Spot : BodyParts.None;
        }

        [HideFromIl2Cpp]
        private void CaptureFrame(PlayerController player)
        {
            var t = player.transform;
            _origin = t.position;
            Vector3 f = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            _frame = f.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(f.normalized, Vector3.up);
            _needFrame = false;
        }

        /// <summary>Tap of F: the circles move between wrists / ankles and elbows / knees.</summary>
        [HideFromIl2Cpp]
        internal void SwitchPlacement()
        {
            if (Machine.State != LockState.Locked) return;
            SetPlacement(Stages.Other(CurrentPlacement), true);
            Say(CurrentPlacement == Placement.ElbowsAndKnees ? "The circles move to her elbows and knees." : "The circles move to her wrists and ankles.");
        }

        internal static string SetPlacement(Placement p, bool fromKeys)
        {
            if (fromKeys) { Plugin.Where.Value = p; _missionPlacement = null; }
            else _missionPlacement = p;
            Plugin.Logger.LogInfo("[SFM-ACCEPT] placement: " + Stages.PlacementName(p) + (fromKeys ? "" : " (mission)"));
            return null;
        }

        internal static bool InPose(PlayerController player)
        {
            var action = player == null || player.Pca == null || player.Pca.PlayerState == null ? null : player.Pca.PlayerState.CurrentAction;
            return action != null && (int)action.Type >= (int)ActionType.GanimataWalk;
        }

        private string _said;
        private float _saidAt = -10f;

        [HideFromIl2Cpp]
        private void Say(string text)
        {
            if (text == _said && Time.unscaledTime - _saidAt < 1.5f) return;
            _said = text;
            _saidAt = Time.unscaledTime;
            GameUi.Message(text);
        }

        // --- pose (LateUpdate) and circles (onPreCull) -----------------------------------------------------------------------

        private Camera.CameraCallback _preCull;
        private int _placedFrame = -1;

        private void Start()
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(OnPreCull));
            Camera.onPreCull = Camera.onPreCull == null
                ? _preCull
                : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
        }

        private void OnDestroy()
        {
            if (_preCull != null && Camera.onPreCull != null)
                Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCull)?.Cast<Camera.CameraCallback>();
            var player = PlayerController.Instance;
            FloatPoser.Unhide(player == null || player.Pca == null ? null : player.Pca.Animator);
            _rig?.Destroy();
            if (Instance == this) Instance = null;
        }

        [HideFromIl2Cpp]
        private FloatSettings Settings() => new FloatSettings
        {
            Height = Plugin.FloatHeight.Value / 100f,
            ArmAngle = Plugin.ArmAngle.Value,
            LegAngle = Plugin.LegAngle.Value,
            BobAmplitude = Plugin.BobAmplitude.Value / 100f,
            BobPeriod = 3.2f,
        };

        private void LateUpdate()
        {
            var player = PlayerController.Instance;
            var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
            if (!Machine.Worn)
            {
                if (FloatPoser.IsHidden) FloatPoser.Unhide(anim);
                return;
            }
            if (anim == null) return;
            try
            {
                FloatPoser.Unhide(anim); // full bones while posing (and for measuring)
                if (!_measured)
                {
                    if (!FloatPoser.Measure(anim, out _body, out string problem))
                    {
                        Plugin.Logger.LogWarning("magic circle taken off: " + problem);
                        Release(problem);
                        return;
                    }
                    _measured = true;
                    EnsureRig(anim);
                }
                if (_needFrame) CaptureFrame(player);
                if ((Plugin.DebugDisable.Value & 1) == 0)
                    FloatPoser.Apply(anim, _body, Settings(), _origin, _frame, Stages.Smooth(_weight), Time.time);
                if (CurrentPlacement == Placement.ElbowsAndKnees && Plugin.HideLimbs.Value && (Plugin.DebugDisable.Value & 2) == 0
                    && Machine.Closed > 0.5f)
                    FloatPoser.Hide(anim, Plugin.PortalBefore.Value / 100f);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("holding her failed, magic circle off: " + e);
                Release("error");
            }
        }

        [HideFromIl2Cpp]
        private void OnPreCull(Camera cam)
        {
            try
            {
                if (_placedFrame == Time.frameCount) return;
                _placedFrame = Time.frameCount;
                if (!Machine.Worn || _rig == null || !_rig.Built) { _rig?.SetVisible(false); return; }
                var player = PlayerController.Instance;
                var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
                if (anim == null) return;
                _rig.SetVisible(true);
                var toesL = anim.GetBoneTransform(HumanBodyBones.LeftToes) ?? anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                var toesR = anim.GetBoneTransform(HumanBodyBones.RightToes) ?? anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (toesL != null && toesR != null) ToesUp = Mathf.Min(toesL.position.y, toesR.position.y) - player.transform.position.y;
                float show = Machine.Closed;
                float t = Time.time;
                float pulse = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t / Plugin.PulseSeconds.Value);
                float spin = t * Plugin.SpinSpeed.Value;
                float room = Plugin.Room.Value;
                bool portal = CurrentPlacement == Placement.ElbowsAndKnees;
                float before = Plugin.PortalBefore.Value / 100f;
                for (int i = 0; i < 4; i++)
                {
                    bool arm = i < 2, left = i % 2 == 0;
                    Transform a, b; // the segment the circle sits on: parent joint -> joint
                    float limb;
                    if (arm)
                    {
                        a = anim.GetBoneTransform(portal ? (left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm) : (left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm));
                        b = anim.GetBoneTransform(portal ? (left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm) : (left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand));
                        // Limb radii from bone lengths (measured on the first save: wrist 2.7 cm, above the elbow 3.5 cm; patterns.md 17a).
                        limb = portal ? 0.13f * _body.UpperArm : 0.12f * _body.Forearm;
                    }
                    else
                    {
                        a = anim.GetBoneTransform(portal ? (left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg) : (left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg));
                        b = anim.GetBoneTransform(portal ? (left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg) : (left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot));
                        limb = portal ? 0.15f * _body.Thigh : 0.09f * _body.Shin;
                    }
                    if (a == null || b == null) continue;
                    var pa = FloatPoser.N(a.position);
                    var pb = FloatPoser.N(b.position);
                    var center = CircleSpot.Before(pa, pb, portal ? (FloatPoser.IsHidden ? 0f : before) : 0.012f);
                    float r = CircleSpot.OuterRadius(limb, room);
                    // Neighbouring circles turn opposite ways.
                    _rig.Place(i, FloatPoser.U(center), b.position - a.position, r, (left ? spin : -spin) + i * 37f, show);
                }
                _rig.Shade(Plugin.Color.Value, Plugin.Glow.Value, pulse, show);
            }
            catch (Exception e) { Fail("placing the circles", e); }
        }

        // --- diagnostics ---------------------------------------------------------------------------------------------------

        public static string PutOnNow() => Instance == null ? "no controller" : Instance.PutOn() ?? "forming";
        public static string Step(int d) => Instance == null ? "no controller" : Instance.SetStage(Instance.Stage + d, true) ?? Status;
        public static string Switch() { Instance?.SwitchPlacement(); return Status; }
        public static string ReleaseNow() { Instance?.Release("bridge"); return Status; }

        /// <summary>Diagnostics: the loaded shaders whose name contains <paramref name="part"/> (case-insensitive).</summary>
        public static string Shaders(string part)
        {
            var names = new System.Collections.Generic.SortedSet<string>();
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Shader>()))
            {
                var s = o.TryCast<Shader>();
                if (s != null && s.name != null && s.name.IndexOf(part ?? "", StringComparison.OrdinalIgnoreCase) >= 0) names.Add(s.name + (s.isSupported ? "" : " (unsupported)"));
            }
            return string.Join(" | ", names);
        }

        private static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
