# Virt-A-Mate Sync Soundtrack with Scene Animation

This will help keep sound and visuals in sync even if your framerate drops.

## Installing

Requires VaM 1.19 or newer.

Download `LFE.SoundtrackSync.(version).var` from [Releases](https://github.com/lfe999/VamSoundtrackSync/releases)

Save the `.var` file in the `(VAM_ROOT)\AddonPackages`.

## Quickstart

### Load a scene that has a soundtrack

Try one of Gatos or IsaacNewtongues scenes that have a soundtrack that play along with some scene animations.

### Find the AudioSource or RhythmAudioSource for the soundtrack

One of these atoms in the scene will be the one that is used for the music as opposed to the sound effects.

Add this plugin to that atom.

### Play the scene

Play the scene and adjust audio offset if needed or any of the other options of this plugin.