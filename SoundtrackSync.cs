// #define LFE_DEBUG
// #define LFE_TRACE
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LFE
{

    public class SoundtrackSync : MVRScript
    {

        private AudioSource _sourceCached;
        private AudioSource Source
        {
            get
            {
                if (_sourceCached == null)
                {
                    var selectedUid = SyncWithAudioPlayingThrough.val;
                    if (!string.IsNullOrEmpty(selectedUid))
                    {
                        var atom = SuperController.singleton.GetAtomByUid(selectedUid);
                        if (atom != null)
                        {
                            // assume the first audio source in the Atom is what we want
                            _sourceCached = atom.GetComponentInChildren<AudioSource>();
                        }
                    }
                }
                return _sourceCached;
            }
        }

        const string FIX_BY_TIME_SCALE = "Change Time Scale";
        const string FIX_BY_AUDIO_TIME_SET = "Set Audio Time";
        const string FIX_BY_AUDIO_PITCH = "Change Audio Pitch";

        private List<string> StrategyChoices = new List<string> {
            FIX_BY_TIME_SCALE,
            FIX_BY_AUDIO_TIME_SET,
            FIX_BY_AUDIO_PITCH
        };

        private List<string> AtomsWithAudioSource
        {
            get
            {
                return containingAtom.transform.root
                    .GetComponentsInChildren<AudioSource>()
                    .Select(src => src.GetComponentInParent<Atom>()?.uid)
                    .Where(uid => !string.IsNullOrEmpty(uid))
                    .ToList();
            }
        }

        const float DriftCorrectIfOver = 0.05f;
        const float DriftStopCorrectIfUnder = 0.01f;
        const float ForceJumpAudioTimeIfOver = 1.0f;
        const float AdjustmentAmount = 0.05f;

        private JSONStorableStringChooser SyncWithAudioPlayingThrough;
        private JSONStorableStringChooser Strategy;
        private JSONStorableFloat SoundtrackOffset;
        private JSONStorableString DebugText;
        private JSONStorableBool JumpAudioIfTooFar;
        private JSONStorableBool StopAudioIfAnimationStopped;
        private UIDynamicTextField DebugTextUi;

        float originalPitch = 0f;
        float originalTimescale = 0f;

        public bool IsUiActive()
        {
            return DebugTextUi?.isActiveAndEnabled ?? false;
        }

        public override void Init()
        {

            var instructions =
                $"Keep a <b>Scene Animation</b> in sync with an <b>AudioSource</b> soundtrack - good if you have some performance issues making things go out of sync.\n\n" +
                $"Make sure that you have some soundtrack audio Play event in a trigger in your <b>Scene Animation</b>.\n\n" +
                $"Add this plugin to any atom in the scene. It will try to find the longest audio file in your <b>Scene Animation</b> and set the <b>Soundtrack From</b> and <b>Audio Offset</b> for you the first time it loads.\n\n" +
                $"<b>SOUNDTRACK FROM:</b>\n" +
                $"Choose which atom that your music will be playing through.\n\n" +
                $"<b>SYNC STRATEGY:</b>\n" +
                $"<b>Change Time Scale</b> - if the audio is behind or ahead, then slow down or speed up the time scale until it is within {DriftStopCorrectIfUnder:0.##} seconds. (Recommended)\n\n" +
                $"<b>Set Audio Time</b> - if the audio is behind or ahead, just set the audio playback time to the animation time. (May sound choppy)\n\n" +
                $"<b>Change Audio Pitch</b> - if the audio is behind or ahead, speed up or slow down the audio pitch until it catches up. (I think this sounds awful)\n\n" +
                $"<b>AUDIO OFFSET</b>:\n" +
                $"Offset the audio from the start of the animation timeline.  For example, if your audio starts 1 second into your animation, set this to 1.\n\n" +
                $"<b>OPTIONS</b>:\n" +
                $"<b>Jump Audio Time If Way Off</b> - if the audio is behind or ahead more that {ForceJumpAudioTimeIfOver} second, just jump the playhead to the correct time even if you have another strategy selected.  This also is nice if you scrub the <b>Scene Animation</b> playhead.  The audio will jump to the right time.\n\n" +
                $"<b>Stop Audio When Animation Stops</b> - if you click 'Stop Animation' try to also pause / resume the related audio\n\n"
                ;

            var instructionUI = CreateTextField(new JSONStorableString("_", instructions), rightSide: true);
            instructionUI.height = 1200;

            var sourceChoices = AtomsWithAudioSource.Concat(new[] { String.Empty }).ToList();
            SyncWithAudioPlayingThrough = new JSONStorableStringChooser("Soundtrack From", sourceChoices, String.Empty, "Soundtrack From", (string val) =>
            {
                // clear the cached audiosource
                _sourceCached = null;
            });
            CreatePopup(SyncWithAudioPlayingThrough);
            RegisterStringChooser(SyncWithAudioPlayingThrough);

            Strategy = new JSONStorableStringChooser("Sync Strategy", StrategyChoices, FIX_BY_TIME_SCALE, "Sync Strategy", (string val) =>
            {
                ResetStrategyDefaults();
            });
            CreatePopup(Strategy);
            RegisterStringChooser(Strategy);

            SoundtrackOffset = new JSONStorableFloat("Audio Offset", 0, -60, 60, constrain: false, interactable: true);
            CreateSlider(SoundtrackOffset);
            RegisterFloat(SoundtrackOffset);

            JumpAudioIfTooFar = new JSONStorableBool("Jump Audio Time If Way Off", true);
            CreateToggle(JumpAudioIfTooFar);
            RegisterBool(JumpAudioIfTooFar);

            StopAudioIfAnimationStopped = new JSONStorableBool("Stop Audio When Animation Stops", true);
            CreateToggle(StopAudioIfAnimationStopped);
            RegisterBool(StopAudioIfAnimationStopped);

            DebugText = new JSONStorableString("DebugText", "");
            DebugTextUi = CreateTextField(DebugText);
        }

        public void Start()
        {
            if (SyncWithAudioPlayingThrough.val == SyncWithAudioPlayingThrough.defaultVal)
            {
                SyncWithAudioPlayingThrough.val = GuessAudioSourceAtom();
#if LFE_DEBUG
                SuperController.LogMessage($"guessed atom: {SyncWithAudioPlayingThrough.val}");
#endif
            }

            if (SoundtrackOffset.val == SoundtrackOffset.defaultVal)
            {
                SoundtrackOffset.val = GuessDefaultOffset();
#if LFE_DEBUG
                SuperController.LogMessage($"guessed offset: {SoundtrackOffset.val}");
#endif
            }

            if (Source == null)
            {
                SuperController.LogError("Could not automatically find any audio sources. Open plugin panel for more instructions.");

                var error =
                    $"No audio sources were automatically found when looking through possible soundtracks in this scene.\n\n" +
                    $"Select the atom that will be playing the main soundtrack audio for your Scene Animation.\n";
                CreateTextField(new JSONStorableString("_error", error));
                return;
            }

            originalPitch = Source.pitch;
            originalTimescale = TimeControl.singleton.currentScale;

        }

        public IEnumerable<KeyValuePair<AnimationTimelineTrigger, TriggerActionDiscrete>> GetAudioStartTriggerActions()
        {
            var mam = SuperController.singleton.motionAnimationMaster;
            // mam.triggers is protected all through the code so, find the actions through the JSON
            var mamJSON = mam.GetJSON();
            try
            {
                foreach (var triggerJSON in mamJSON["triggers"].AsArray.Cast<SimpleJSON.JSONClass>())
                {
                    var trigger = new AnimationTimelineTrigger();
                    trigger.RestoreFromJSON(triggerJSON);
                    foreach (var actionJSON in triggerJSON["startActions"].AsArray.Cast<SimpleJSON.JSONClass>())
                    {
                        var action = new TriggerActionDiscrete();
                        action.RestoreFromJSON(actionJSON);

                        if (action.audioClip != null)
                        {
                            yield return new KeyValuePair<AnimationTimelineTrigger, TriggerActionDiscrete>(trigger, action);
                        }
                    }
                }
            }
            finally { }
            yield break;
        }

        public float GuessDefaultOffset()
        {
            var atomUid = SyncWithAudioPlayingThrough.val;
            if (string.IsNullOrEmpty(atomUid))
            {
                return 0;
            }

            var audioTriggersForAtom = GetAudioStartTriggerActions()
                .Where(act => act.Value.receiverAtom.uid == atomUid)
                .OrderByDescending(act => act.Value.audioClip.sourceClip.length)
                .ToList();
            if (audioTriggersForAtom.Count == 1)
            {
                return audioTriggersForAtom[0].Key.triggerStartTime;
            }

            return 0;
        }

        public string GuessAudioSourceAtom()
        {
            var audioTriggers = GetAudioStartTriggerActions()
                .OrderByDescending(act => act.Value.audioClip.sourceClip.length)
                .ToList();
            if (audioTriggers.Count > 0)
            {
                return audioTriggers[0].Value.receiverAtom.uid;
            }
            else
            {
                if (containingAtom.GetComponentInChildren<AudioSource>() != null)
                {
                    return containingAtom.uid;
                }
            }
            return String.Empty;
        }

        float currentAdjustment = 0f;
        float previousDrift = 0f;

        float debugCounter = 0f;

        private void HandleAudioPitch()
        {
            var animationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter();

            var currentDrift = CurrentDrift();
            var currentDriftAbsolute = Math.Abs(currentDrift);
            var previousDriftAbsolute = Math.Abs(previousDrift);

            bool undergoingAdjustment = currentAdjustment != 0;
            bool shouldStartAdjustment = !undergoingAdjustment && currentDriftAbsolute >= DriftCorrectIfOver;
            bool shouldStopAdjustment = undergoingAdjustment && currentDriftAbsolute <= DriftStopCorrectIfUnder;
            bool shouldChangeAdjustmentStrength = undergoingAdjustment && currentDriftAbsolute != previousDriftAbsolute;
            if (shouldStartAdjustment)
            {
                var adjustBy = Math.Sign(currentDrift) * -1 * AdjustmentAmount * Time.deltaTime;
#if LFE_DEBUG
                SuperController.LogMessage($"PITCH audioTime = {Source.time} audioMax = {Source.clip.length} animationTime = {animationTime} drift = {currentDriftAbsolute} adjustment = {adjustBy}");
#endif

                currentAdjustment += adjustBy;
                Source.pitch += adjustBy;

            }
            else if (shouldStopAdjustment)
            {
                Source.pitch = originalPitch;
                currentAdjustment = 0;
#if LFE_DEBUG
                SuperController.LogMessage($"PITCH audioTime = {Source.time} audioMax = {Source.clip.length} animationTime = {animationTime} drift = {currentDriftAbsolute} adjustment = 0");
#endif
            }
            else if (shouldChangeAdjustmentStrength)
            {
                var adjustBy = Math.Sign(currentDrift) * -1 * AdjustmentAmount * Time.deltaTime;
                if (currentDriftAbsolute > previousDriftAbsolute)
                {
                    // things are getting worse! - increase our fixing
                    currentAdjustment += adjustBy;
                    Source.pitch += adjustBy;
#if LFE_DEBUG
                    SuperController.LogMessage($"PITCH audioTime = {Source.time} audioMax = {Source.clip.length} animationTime = {animationTime} drift = {currentDriftAbsolute} adjustment = {adjustBy}");
#endif
                }
            }
            if (IsUiActive())
            {
                UpdateDebugInfo(animationTime, currentDrift, currentAdjustment, TimeControl.singleton.currentScale, Source.pitch);
            }
        }

        private void HandleAudioTimeJump()
        {
            var animationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter();

            var currentDrift = CurrentDrift();
            var currentDriftAbsolute = Math.Abs(currentDrift);

            if (currentDriftAbsolute >= DriftCorrectIfOver * 0.5f)
            {
                var newTime = TargetAudioSourceTime();
#if LFE_DEBUG
                SuperController.LogMessage($"AUDIOJUMP audioTime = {Source.time} audioMax = {Source.clip.length} animationTime = {animationTime} drift = {currentDriftAbsolute} (over {DriftCorrectIfOver * 0.5f}) newTime = {newTime}");
#endif
                Source.time = newTime;
                currentAdjustment = 0;
            }
            if (IsUiActive())
            {
                UpdateDebugInfo(animationTime, currentDrift, currentAdjustment, TimeControl.singleton.currentScale, Source.pitch);
            }
        }

        private void HandleTimescale()
        {
            var animationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter();

            var currentDrift = CurrentDrift();
            var currentDriftAbsolute = Math.Abs(currentDrift);
            var previousDriftAbsolute = Math.Abs(previousDrift);

            bool undergoingAdjustment = currentAdjustment != 0;
            bool shouldStartAdjustment = !undergoingAdjustment && currentDriftAbsolute >= DriftCorrectIfOver;
            bool shouldStopAdjustment = undergoingAdjustment && currentDriftAbsolute <= DriftStopCorrectIfUnder;
            bool shouldChangeAdjustmentStrength = undergoingAdjustment && currentDriftAbsolute != previousDriftAbsolute;
            if (shouldStartAdjustment)
            {
                var adjustBy = Math.Sign(currentDrift) * AdjustmentAmount * Time.deltaTime * 10;
#if LFE_DEBUG
                SuperController.LogMessage($"TIMESCALE audioTime = {Source.time} audioMax = {Source.clip.length} animationTime = {animationTime} drift = {currentDriftAbsolute} adjustment = {adjustBy}");
#endif

                currentAdjustment += adjustBy;
                TimeControl.singleton.currentScale = originalTimescale + currentAdjustment;

            }
            else if (shouldStopAdjustment)
            {
                TimeControl.singleton.currentScale = originalTimescale;
                currentAdjustment = 0;
#if LFE_DEBUG
                SuperController.LogMessage($"TIMESCALE audioTime = {Source.time} audioMax = {Source.clip.length} animationTime = {animationTime} drift = {currentDriftAbsolute} adjustment = 0");
#endif
            }
            else if (shouldChangeAdjustmentStrength)
            {
                if (currentDriftAbsolute > previousDriftAbsolute)
                {
                    var adjustBy = Math.Sign(currentDrift) * AdjustmentAmount * Time.deltaTime * 10;
                    // things are getting worse!
                    currentAdjustment += adjustBy;
                    TimeControl.singleton.currentScale = originalTimescale + currentAdjustment;
#if LFE_DEBUG
                    SuperController.LogMessage($"TIMESCALE audioTime = {Source.time} audioMax = {Source.clip.length} animationTime = {animationTime} drift = {currentDriftAbsolute} adjustment = {adjustBy}");
#endif
                }
            }
            if (IsUiActive())
            {
                UpdateDebugInfo(animationTime, currentDrift, currentAdjustment, TimeControl.singleton.currentScale, Source.pitch);
            }
        }

        public void UpdateDebugInfo(float animationTime, float drift, float adjustment, float timescale, float pitch)
        {
            debugCounter += Time.deltaTime;
            if (debugCounter > 0.25f)
            {
                string driftOverageText = drift > DriftCorrectIfOver ? "(over limit)" : "";
                DebugText.val = $"animation time: {animationTime:0.###}\ndrift: {drift:0.###} {driftOverageText}\nadjustment: {adjustment}\ntime scale: {timescale}\npitch: {pitch}";
                debugCounter = 0;
            }
        }


        float previousAnimationTime = -1f;

        private void Update()
        {

            if (SuperController.singleton.freezeAnimation)
            {
                return;
            }

            if (Source == null)
            {
                return;
            }

            var strategy = Strategy.valNoCallback;
            if (string.IsNullOrEmpty(strategy))
            {
                return;
            }

            var audioTime = Source.time;
            var animationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter();

            if (previousAnimationTime == animationTime)
            {
                // animation is stopped
                if (StopAudioIfAnimationStopped.val)
                {
                    Source.Pause();
                }
                return;
            }

            if (!Source.isPlaying)
            {
                if (StopAudioIfAnimationStopped.val && Source.time > 0)
                {
                    Source.UnPause();
                }
                else
                {
                    Source.time = 0;
                    return;
                }
            }

            var currentDrift = CurrentDrift();

            if (JumpAudioIfTooFar.val && Math.Abs(currentDrift) > ForceJumpAudioTimeIfOver)
            {
                HandleAudioTimeJump();
            }
            else
            {
                if (strategy == FIX_BY_AUDIO_PITCH)
                {
                    HandleAudioPitch();
                }
                else if (strategy == FIX_BY_AUDIO_TIME_SET)
                {
                    HandleAudioTimeJump();
                }
                else if (strategy == FIX_BY_TIME_SCALE)
                {
                    HandleTimescale();
                }
            }

            previousDrift = currentDrift;
            previousAnimationTime = animationTime;
        }

        private void ResetStrategyDefaults()
        {
            if (Source != null)
            {
                Source.pitch = originalPitch;
            }
            TimeControl.singleton.currentScale = originalTimescale;

            previousAnimationTime = -1;
            currentAdjustment = 0f;
            previousDrift = 0f;
            debugCounter = 0f;
        }

        private float TargetAudioSourceTime()
        {
            var target = 0f;

            var offsetAnimationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter() - SoundtrackOffset.val;
            if (offsetAnimationTime < 0)
            {
                target = 0f;
            }
            else
            {
                if (Source.loop)
                {
                    target = offsetAnimationTime % Source.clip.length;
                }
                else
                {
                    target = Mathf.Min(offsetAnimationTime, Source.clip.length);
                }
            }
#if LFE_TRACE
            SuperController.LogMessage($"TargetAudioSourceTime = {target} offsetAnimationTime = {offsetAnimationTime}");
#endif
            return target;
        }

        private float CurrentDrift()
        {
            return Source.time - TargetAudioSourceTime();
        }

        private void OnDestroy()
        {
            ResetStrategyDefaults();
        }
    }
}
