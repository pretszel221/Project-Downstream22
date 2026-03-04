// SPDX-FileCopyrightText: 2022 ike709 <ike709@github.com>
// SPDX-FileCopyrightText: 2022 ike709 <ike709@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Pieter-Jan Briers <pieterjan.briers@gmail.com>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Pieter-Jan Briers <pieterjan.briers+git@gmail.com>
// SPDX-FileCopyrightText: 2024 Tadeo <td12233a@gmail.com>
// SPDX-FileCopyrightText: 2025 Tay <td12233a@gmail.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.Client.Popups;
using Content.Shared.Audio;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client.Audio;

public sealed class ClientGlobalSoundSystem : SharedGlobalSoundSystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // Admin music, with per-stream base volume so CVar changes affect already-playing streams.
    private bool _adminAudioEnabled = true;
    private float _adminMusicVolume = 1f;
    private float _adminMusicVolumeOffset;
    private readonly Dictionary<EntityUid, float> _adminAudio = new();
    private readonly List<EntityUid> _adminAudioToRemove = new();
    private TimeSpan _nextAdminStreamPrune = TimeSpan.Zero;

    // Event sounds (e.g. nuke timer)
    private bool _eventAudioEnabled = true;
    private Dictionary<StationEventMusicType, EntityUid?> _eventAudio = new(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeNetworkEvent<AdminSoundEvent>(PlayAdminSound);
        Subs.CVar(_cfg, CCVars.AdminMusicEnabled, ToggleAdminSound, true);
        Subs.CVar(_cfg, CCVars.AdminMusicVolume, SetAdminMusicVolume, true);

        SubscribeNetworkEvent<StationEventMusicEvent>(PlayStationEventMusic);
        SubscribeNetworkEvent<StopStationEventMusic>(StopStationEventMusic);
        Subs.CVar(_cfg, CCVars.EventMusicEnabled, ToggleStationEventMusic, true);

        SubscribeNetworkEvent<GameGlobalSoundEvent>(PlayGameSound);
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextAdminStreamPrune)
            return;

        _nextAdminStreamPrune = _timing.CurTime + TimeSpan.FromSeconds(2);
        PruneFinishedAdminStreams();
    }

    private void PruneFinishedAdminStreams()
    {
        if (_adminAudio.Count == 0)
            return;

        _adminAudioToRemove.Clear();

        foreach (var stream in _adminAudio.Keys)
        {
            if (Deleted(stream) || !TryComp(stream, out AudioComponent? audioComp) || !audioComp.Playing)
                _adminAudioToRemove.Add(stream);
        }

        foreach (var stream in _adminAudioToRemove)
        {
            _adminAudio.Remove(stream);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        ClearAudio();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ClearAudio();
    }

    private void ClearAudio()
    {
        foreach (var stream in _adminAudio.Keys)
        {
            _audio.Stop(stream);
        }
        _adminAudio.Clear();

        foreach (var stream in _eventAudio.Values)
        {
            _audio.Stop(stream);
        }

        _eventAudio.Clear();
    }

    private void PlayAdminSound(AdminSoundEvent soundEvent)
    {
        if (!_adminAudioEnabled)
            return;

        var baseParameters = soundEvent.AudioParams ?? AudioParams.Default;
        var parameters = baseParameters.WithVolume(baseParameters.Volume + _adminMusicVolumeOffset);

        var stream = _audio.PlayGlobal(soundEvent.Specifier, Filter.Local(), false, parameters);

        if (stream?.Entity is { } streamUid)
            _adminAudio[streamUid] = baseParameters.Volume;

        if (!string.IsNullOrWhiteSpace(soundEvent.PlayedBy))
        {
            var mode = soundEvent.LocalPlayback
                ? Loc.GetString("admin-music-popup-local", ("range", (int) soundEvent.Range))
                : Loc.GetString("admin-music-popup-global");

            var track = soundEvent.TrackLabel ?? Loc.GetString("admin-music-popup-track-unknown");
            _popup.PopupCursor(Loc.GetString("admin-music-popup", ("admin", soundEvent.PlayedBy), ("mode", mode), ("track", track)));
        }
    }

    private void PlayStationEventMusic(StationEventMusicEvent soundEvent)
    {
        // Either the cvar is disabled or it's already playing
        if(!_eventAudioEnabled || _eventAudio.ContainsKey(soundEvent.Type)) return;

        var stream = _audio.PlayGlobal(soundEvent.Specifier, Filter.Local(), false, soundEvent.AudioParams);
        _eventAudio.Add(soundEvent.Type, stream?.Entity);
    }

    private void PlayGameSound(GameGlobalSoundEvent soundEvent)
    {
        _audio.PlayGlobal(soundEvent.Specifier, Filter.Local(), false, soundEvent.AudioParams);
    }

    private void StopStationEventMusic(StopStationEventMusic soundEvent)
    {
        if (!_eventAudio.TryGetValue(soundEvent.Type, out var stream))
            return;

        _audio.Stop(stream);
        _eventAudio.Remove(soundEvent.Type);
    }

    private void ToggleAdminSound(bool enabled)
    {
        _adminAudioEnabled = enabled;
        if (_adminAudioEnabled)
            return;

        foreach (var stream in _adminAudio.Keys)
        {
            _audio.Stop(stream);
        }

        _adminAudio.Clear();
    }

    private void SetAdminMusicVolume(float volume)
    {
        if (MathHelper.CloseToPercent(_adminMusicVolume, volume))
            return;

        _adminMusicVolume = volume;
        _adminMusicVolumeOffset = SharedAudioSystem.GainToVolume(_adminMusicVolume);
        PruneFinishedAdminStreams();

        foreach (var (stream, baseVolume) in _adminAudio)
        {
            _audio.SetVolume(stream, baseVolume + _adminMusicVolumeOffset);
        }
    }

    private void ToggleStationEventMusic(bool enabled)
    {
        _eventAudioEnabled = enabled;
        if (_eventAudioEnabled) return;
        foreach (var stream in _eventAudio)
        {
            _audio.Stop(stream.Value);
        }
        _eventAudio.Clear();
    }
}
