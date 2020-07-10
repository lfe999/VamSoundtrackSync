using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LFE {

	public class SoundtrackSync : MVRScript {

        AudioSource Source;

        const string FIX_BY_TIME_SCALE = "Change Time Scale";
        const string FIX_BY_AUDIO_TIME_SET = "Set Audio Time";
        const string FIX_BY_AUDIO_PITCH = "Change Audio Pitch";

        private List<string> StrategyChoices = new List<string> {
            FIX_BY_TIME_SCALE,
            FIX_BY_AUDIO_TIME_SET,
            FIX_BY_AUDIO_PITCH
        };

        const float DriftCorrectIfOver = 0.05f;
        const float DriftStopCorrectIfUnder = 0.01f;
        const float ForceJumpAudioTimeIfOver = 1.0f;
        const float AdjustmentAmount = 0.05f;

        private JSONStorableStringChooser Strategy;
        private JSONStorableFloat SoundtrackOffset;
        private JSONStorableString DebugText;
        private JSONStorableBool JumpAudioIfTooFar;
        private JSONStorableBool StopAudioIfAnimationStopped;
        private UIDynamicTextField DebugTextUi;

        float originalPitch;
        float originalTimescale;

        public bool IsUiActive() {
            return DebugTextUi?.isActiveAndEnabled ?? false;
        }

        public override void Init() {

            var instructions =
                $"Keep a <b>Scene Animation</b> in sync with an <b>AudioSource</b> soundtrack - good if you have some performance issues making things go out of sync.\n\n" +
                $"Add this plugin to the <b>AudioSource</b> or <b>RhythmAudioSource</b> atom that will be playing your soundtrack.\n\n" +
                $"This assumes that you have some soundtrack audio that starts at the beginning of your Scene Animation (no offset supported yet).\n\n" +
                $"Strategies:\n" +
                $"<b>Change Time Scale</b> - if the audio is behind or ahead, then slow down or speed up the time scale until it is within {DriftStopCorrectIfUnder:0.##} seconds. (Recommended)\n\n" +
                $"<b>Set Audio Time</b> - if the audio is behind or ahead, just set the audio playback time to the animation time. (May sound choppy)\n\n" +
                $"<b>Change Audio Pitch</b> - if the audio is behind or ahead, speed up or slow down the audio pitch until it catches up. (I think this sounds awful)\n\n" +
                $"Options:\n" +
                $"<b>Jump Audio Time If Way Off</b> - if the audio is behind or ahead more that {ForceJumpAudioTimeIfOver} second, just jump the playhead to the correct time even if you have another strategy selected.  This also is nice if you scrub the <b>Scene Animation</b> playhead.  The audio will jump to the right time.\n\n" +
                $"<b>Stop Audio When Animation Stops</b> - if you click 'Stop Animation' try to also pause / resume the related audio\n\n"
                ;

            var instructionUI = CreateTextField(new JSONStorableString("_", instructions), rightSide: true);
            instructionUI.height = 1200;

            Source = FindFirstAudioSource();
            if(Source == null) {
                SuperController.LogError("Could not find any audio sources on this atom. Open plugin panel for more instructions.");

                var error =
                    $"No audio sources were found on this atom.\n\n" +
                    $"Put this on an atom that will be playing the audio for your Scene Animation.\n\n" +
                    $"For example, put this on an AudioSource or RhythmAudioSource atom";
                CreateTextField(new JSONStorableString("_error", error));
                return;
            }

            originalPitch = Source.pitch;
            originalTimescale = TimeControl.singleton.currentScale;

            Strategy = new JSONStorableStringChooser("Sync Strategy", StrategyChoices, FIX_BY_TIME_SCALE, "Sync Strategy", (string val) => {
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

        public AudioSource FindFirstAudioSource() {
            var audioSources = containingAtom.GetComponentsInChildren<AudioSource>();
            if(audioSources.Length == 0) {
                SuperController.LogError("no audio source found");
                return null;
            }
            return audioSources[0];
        }

        float currentAdjustment = 0f;
        float previousDrift = 0f;

        float debugCounter = 0f;

        private void HandleAudioPitch() {
            var audioTime = Source.time;
            var animationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter();

            var currentDrift = audioTime - animationTime + SoundtrackOffset.val;
            var currentDriftAbsolute = Math.Abs(currentDrift);
            var previousDriftAbsolute = Math.Abs(previousDrift);

            bool undergoingAdjustment = currentAdjustment != 0;
            bool shouldStartAdjustment = !undergoingAdjustment && currentDriftAbsolute >= DriftCorrectIfOver;
            bool shouldStopAdjustment = undergoingAdjustment && currentDriftAbsolute <= DriftStopCorrectIfUnder;
            bool shouldChangeAdjustmentStrength = undergoingAdjustment && currentDriftAbsolute != previousDriftAbsolute;
            if(shouldStartAdjustment) {
                var adjustBy = Math.Sign(currentDrift) * -1 * AdjustmentAmount * Time.deltaTime;

                currentAdjustment += adjustBy;
                Source.pitch += adjustBy;

            }
            else if(shouldStopAdjustment) {
                Source.pitch = originalPitch;
                currentAdjustment = 0;
            }
            else if(shouldChangeAdjustmentStrength) {
                var adjustBy = Math.Sign(currentDrift) * -1 * AdjustmentAmount * Time.deltaTime;
                if(currentDriftAbsolute > previousDriftAbsolute) {
                    // things are getting worse! - increase our fixing
                    currentAdjustment += adjustBy;
                    Source.pitch += adjustBy;
                }
            }
            UpdateDebugInfo(animationTime, currentDrift, currentAdjustment, TimeControl.singleton.currentScale, Source.pitch);
        }

        private void HandleAudioTimeJump() {
            var audioTime = Source.time;
            var animationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter();

            var currentDrift = audioTime - animationTime + SoundtrackOffset.val;
            var currentDriftAbsolute = Math.Abs(currentDrift);
            if(currentDriftAbsolute >= DriftCorrectIfOver * 0.5f) {
                var newTime = animationTime - SoundtrackOffset.val;
                if(newTime < 0) {
                    newTime = 0;
                }
                Source.time = newTime;
                currentAdjustment = 0;
            }
            UpdateDebugInfo(animationTime, currentDrift, currentAdjustment, TimeControl.singleton.currentScale, Source.pitch);
        }

        private void HandleTimescale() {
            var audioTime = Source.time;
            var animationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter();

            var currentDrift = audioTime - animationTime + SoundtrackOffset.val;
            var currentDriftAbsolute = Math.Abs(currentDrift);
            var previousDriftAbsolute = Math.Abs(previousDrift);

            bool undergoingAdjustment = currentAdjustment != 0;
            bool shouldStartAdjustment = !undergoingAdjustment && currentDriftAbsolute >= DriftCorrectIfOver;
            bool shouldStopAdjustment = undergoingAdjustment && currentDriftAbsolute <= DriftStopCorrectIfUnder;
            bool shouldChangeAdjustmentStrength = undergoingAdjustment && currentDriftAbsolute != previousDriftAbsolute;
            if(shouldStartAdjustment) {
                var adjustBy = Math.Sign(currentDrift) * AdjustmentAmount * Time.deltaTime * 10;

                currentAdjustment += adjustBy;
                TimeControl.singleton.currentScale += adjustBy;

            }
            else if(shouldStopAdjustment) {
                TimeControl.singleton.currentScale = originalTimescale;
                currentAdjustment = 0;
            }
            else if(shouldChangeAdjustmentStrength) {
                var adjustBy = Math.Sign(currentDrift) * AdjustmentAmount * Time.deltaTime * 10;
                if(currentDriftAbsolute > previousDriftAbsolute) {
                    // things are getting worse!
                    currentAdjustment += adjustBy;
                    TimeControl.singleton.currentScale += adjustBy;
                }
            }
            UpdateDebugInfo(animationTime, currentDrift, currentAdjustment, TimeControl.singleton.currentScale, Source.pitch);
        }

        public void UpdateDebugInfo(float animationTime, float drift, float adjustment, float timescale, float pitch) {
            if(!IsUiActive()) {
                return;
            }
            debugCounter += Time.deltaTime;
            if(debugCounter > 0.25f) {
                string driftOverageText = drift > DriftCorrectIfOver ? "(over limit)" : "";
                DebugText.val = $"animation time: {animationTime:0.###}\ndrift: {drift:0.###} {driftOverageText}\nadjustment: {adjustment}\ntime scale: {timescale}\npitch: {pitch}";
                debugCounter = 0;
            }
        }


        float previousAnimationTime = -1f;

		private void Update() {


            if(SuperController.singleton.freezeAnimation) {
                return;
            }


            var strategy = Strategy.valNoCallback;
            if(string.IsNullOrEmpty(strategy)) {
                return;
            }

            var audioTime = Source.time;
            var animationTime = SuperController.singleton.motionAnimationMaster.GetCurrentTimeCounter();

            if(previousAnimationTime == animationTime) {
                // animation is stopped
                if(StopAudioIfAnimationStopped.val) {
                    Source.Pause();
                }
                return;
            }

            if(!Source.isPlaying) {
                if(StopAudioIfAnimationStopped.val && Source.time > 0) {
                    Source.UnPause();
                }
                else {
                    Source.time = 0;
                    return;
                }
            }

            var currentDrift = audioTime - animationTime + SoundtrackOffset.val;

            if(JumpAudioIfTooFar.val && Math.Abs(currentDrift) > ForceJumpAudioTimeIfOver) {
                HandleAudioTimeJump();
            }
            else {
                if(strategy == FIX_BY_AUDIO_PITCH) {
                    HandleAudioPitch();
                }
                else if(strategy == FIX_BY_AUDIO_TIME_SET) {
                    HandleAudioTimeJump();
                }
                else if(strategy == FIX_BY_TIME_SCALE) {
                    HandleTimescale();
                }
            }

            previousDrift = currentDrift;
            previousAnimationTime = animationTime;
        }

        private void ResetStrategyDefaults() {
            if(Source != null) {
                Source.pitch = originalPitch;
            }
            TimeControl.singleton.currentScale = originalTimescale;
        }

        private void OnDestroy() {
            ResetStrategyDefaults();
        }
    }
}
