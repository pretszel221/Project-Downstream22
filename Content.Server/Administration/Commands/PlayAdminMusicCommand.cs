// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

using System;
using System.Globalization;
using System.IO;
using Content.Server.Audio;
using Content.Shared.Administration;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed class PlayAdminMusicCommand : IConsoleCommand
{
    private const int MinVolumeOffset = -40;
    private const int MaxVolumeOffset = 20;
    private const float MinLocalRange = 1f;
    private const float MaxLocalRange = 40f;
    private const int MaxTrackLabelLength = 96;

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IResourceManager _resource = default!;

    public string Command => "playadminmusic";
    public string Description => Loc.GetString("play-admin-music-command-description");
    public string Help => Loc.GetString("play-admin-music-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        var mode = args[0].ToLowerInvariant();
        if (mode != "global" && mode != "local")
        {
            shell.WriteError(Loc.GetString("play-admin-music-command-invalid-mode", ("mode", args[0])));
            return;
        }

        if (!TryResolveMusicInput(args[1], out var path, out var trackLabel, out var error))
        {
            shell.WriteError(error);
            return;
        }

        var globalSoundSystem = _entManager.System<ServerGlobalSoundSystem>();
        var sharedAudioSystem = _entManager.System<SharedAudioSystem>();
        var transformSystem = _entManager.System<TransformSystem>();

        var audio = AudioParams.Default.AddVolume(-8);
        if (args.Length >= 3)
        {
            if (!int.TryParse(args[2], out var dbOffset))
            {
                shell.WriteError(Loc.GetString("play-admin-music-command-volume-parse", ("volume", args[2])));
                return;
            }

            if (dbOffset is < MinVolumeOffset or > MaxVolumeOffset)
            {
                shell.WriteError(Loc.GetString("play-admin-music-command-volume-range", ("min", MinVolumeOffset), ("max", MaxVolumeOffset)));
                return;
            }

            audio = audio.AddVolume(dbOffset);
        }

        var range = 12f;
        if (mode == "local" && args.Length >= 4)
        {
            if (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out range))
            {
                shell.WriteError(Loc.GetString("play-admin-music-command-range-parse", ("range", args[3])));
                return;
            }

            if (range is < MinLocalRange or > MaxLocalRange)
            {
                shell.WriteError(Loc.GetString("play-admin-music-command-range-limits", ("min", MinLocalRange), ("max", MaxLocalRange)));
                return;
            }
        }

        var specifier = sharedAudioSystem.ResolveSound(new SoundPathSpecifier(path));
        var byAdmin = shell.Player?.Name;

        if (mode == "global")
        {
            var filter = Filter.Empty().AddAllPlayers(_playerManager);
            globalSoundSystem.PlayAdminGlobal(filter, specifier, audio, true, byAdmin, trackLabel, false, 0f);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } attached)
        {
            shell.WriteError(Loc.GetString("play-admin-music-command-local-requires-body"));
            return;
        }

        var mapCoordinates = transformSystem.GetMapCoordinates(attached);
        var localFilter = Filter.Empty().AddInRange(mapCoordinates, range);

        globalSoundSystem.PlayAdminGlobal(localFilter, specifier, audio, true, byAdmin, trackLabel, true, range);
    }

    private bool TryResolveMusicInput(string input, out string path, out string label, out string error)
    {
        label = Path.GetFileNameWithoutExtension(input).Trim();
        if (label.Length > MaxTrackLabelLength)
            label = label[..MaxTrackLabelLength];

        if (!input.StartsWith('/') ||
            !input.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("..", StringComparison.Ordinal) ||
            input.Contains('\\', StringComparison.Ordinal))
        {
            path = string.Empty;
            error = Loc.GetString("play-admin-music-command-invalid-path", ("path", input));
            return false;
        }

        if (_resource.TryContentFileRead(input, out var stream))
        {
            stream.Dispose();
            path = input;
            error = string.Empty;
            return true;
        }

        path = string.Empty;
        error = Loc.GetString("play-admin-music-command-missing-path", ("path", input));
        return false;
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(new[] { "global", "local" }, Loc.GetString("play-admin-music-command-arg-mode"));

        if (args.Length == 2)
            return CompletionResult.FromHint(Loc.GetString("play-admin-music-command-arg-path"));

        if (args.Length == 3)
            return CompletionResult.FromHint(Loc.GetString("play-admin-music-command-arg-volume"));

        if (args.Length == 4)
            return CompletionResult.FromHint(Loc.GetString("play-admin-music-command-arg-range"));

        return CompletionResult.Empty;
    }
}
