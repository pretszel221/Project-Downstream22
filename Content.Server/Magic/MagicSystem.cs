// SPDX-FileCopyrightText: 2022 Jessica M <jessica@jessicamaybe.com>
// SPDX-FileCopyrightText: 2022 Jezithyr <Jezithyr@gmail.com>
// SPDX-FileCopyrightText: 2022 Kara <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2022 Rane <60792108+Elijahrane@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <comedian_vs_clown@hotmail.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <metalgearsloth@gmail.com>
// SPDX-FileCopyrightText: 2023 AJCM-git <60196617+AJCM-git@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Ben <50087092+benev0@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 BenOwnby <ownbyb@appstate.edu>
// SPDX-FileCopyrightText: 2023 Dexler <69513582+DexlerXD@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <drsmugleaf@gmail.com>
// SPDX-FileCopyrightText: 2023 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Pieter-Jan Briers <pieterjan.briers@gmail.com>
// SPDX-FileCopyrightText: 2023 Tom Leys <tom@crump-leys.com>
// SPDX-FileCopyrightText: 2023 Visne <39844191+Visne@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Kira Bridgeton <161087999+Verbalase@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 PoTeletubby <108604614+PoTeletubby@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 PoTeletubby <ajcraigaz@gmail.com>
// SPDX-FileCopyrightText: 2024 Tayrtahn <tayrtahn@gmail.com>
// SPDX-FileCopyrightText: 2024 TemporalOroboros <TemporalOroboros@gmail.com>
// SPDX-FileCopyrightText: 2024 keronshb <54602815+keronshb@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 nikthechampiongr <32041239+nikthechampiongr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Tay <td12233a@gmail.com>
// SPDX-FileCopyrightText: 2025 Terkala <appleorange64@gmail.com>
// SPDX-FileCopyrightText: 2025 pa.pecherskij <pa.pecherskij@interfax.ru>
// SPDX-FileCopyrightText: 2025 slarticodefast <161409025+slarticodefast@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later OR MIT

using Content.Server.Antag;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Ghost;
using Content.Server.Roles;
using Content.Shared.Magic;
using Content.Shared.Magic.Events;
using Content.Shared.Mind;
using Content.Shared.Revolutionary.Components;
using Content.Shared.Roles;
using Content.Shared.Tag;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Magic;

public sealed class MagicSystem : SharedMagicSystem
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly ProtoId<TagPrototype> InvalidForSurvivorAntagTag = "InvalidForSurvivorAntag";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpeakSpellEvent>(OnSpellSpoken);
        SubscribeLocalEvent<TrueChaosSpellEvent>(OnTrueChaosSpell);
    }

    private void OnSpellSpoken(ref SpeakSpellEvent args)
    {
        _chat.TrySendInGameICMessage(args.Performer, Loc.GetString(args.Speech), InGameICChatType.Speak, false);
    }

    public override void OnVoidApplause(VoidApplauseSpellEvent ev)
    {
        base.OnVoidApplause(ev);

        _chat.TryEmoteWithChat(ev.Performer, ev.Emote);

        var perfXForm = Transform(ev.Performer);
        var targetXForm = Transform(ev.Target);

        Spawn(ev.Effect, perfXForm.Coordinates);
        Spawn(ev.Effect, targetXForm.Coordinates);
    }

    protected override void OnRandomGlobalSpawnSpell(RandomGlobalSpawnSpellEvent ev)
    {
        base.OnRandomGlobalSpawnSpell(ev);

        if (!ev.MakeSurvivorAntagonist)
            return;

        if (_mind.TryGetMind(ev.Performer, out var mind, out _) && !_tag.HasTag(mind, InvalidForSurvivorAntagTag))
            _tag.AddTag(mind, InvalidForSurvivorAntagTag);

        EntProtoId survivorRule = "Survivor";

        if (!_gameTicker.IsGameRuleActive<SurvivorRuleComponent>())
            _gameTicker.StartGameRule(survivorRule);
    }

    private enum TrueChaosRole
    {
        SyndicateAgent,
        Thief,
        InitialInfected,
        Revolutionary,
        HeadRevolutionary,
        Changeling,
    }

    private void OnTrueChaosSpell(ref TrueChaosSpellEvent ev)
    {
        if (ev.Handled)
            return;

        var candidates = new List<ICommonSession>();
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } attached)
                continue;

            if (!_mind.TryGetMind(attached, out var mindId, out _))
                continue;

            if (_roles.MindHasRole<ObserverRoleComponent>(mindId))
                continue;

            candidates.Add(session);
        }

        var headRevAvailable = true;
        foreach (var session in candidates)
        {
            if (session.AttachedEntity is not { Valid: true } attached || !_mind.TryGetMind(attached, out var mindId, out _))
                continue;

            var role = PickTrueChaosRole(headRevAvailable);
            if (role == TrueChaosRole.HeadRevolutionary)
                headRevAvailable = false;

            AssignTrueChaosRole(session, attached, mindId, role);
            EnsureComp<ExcludeFromRoundEndSummaryComponent>(mindId);
        }

        Speak(ev);
        ev.Handled = true;
    }

    private TrueChaosRole PickTrueChaosRole(bool headRevAvailable)
    {
        var possible = new List<TrueChaosRole>
        {
            TrueChaosRole.SyndicateAgent,
            TrueChaosRole.Thief,
            TrueChaosRole.InitialInfected,
            TrueChaosRole.Revolutionary,
            TrueChaosRole.Changeling,
        };

        if (headRevAvailable)
            possible.Add(TrueChaosRole.HeadRevolutionary);

        return _random.Pick(possible);
    }

    private void AssignTrueChaosRole(ICommonSession session, EntityUid attached, EntityUid mindId, TrueChaosRole role)
    {
        switch (role)
        {
            case TrueChaosRole.SyndicateAgent:
                _antag.ForceMakeAntag<TraitorRuleComponent>(session, "Traitor");
                break;
            case TrueChaosRole.Thief:
                _antag.ForceMakeAntag<ThiefRuleComponent>(session, "Thief");
                break;
            case TrueChaosRole.InitialInfected:
                _antag.ForceMakeAntag<ZombieRuleComponent>(session, "medZombies");
                break;
            case TrueChaosRole.Revolutionary:
                EnsureComp<RevolutionaryComponent>(attached);
                _roles.MindAddRole(mindId, "MindRoleRevolutionary", silent: true);
                break;
            case TrueChaosRole.HeadRevolutionary:
                _antag.ForceMakeAntag<RevolutionaryRuleComponent>(session, "Revolutionary");
                break;
            case TrueChaosRole.Changeling:
                _antag.ForceMakeAntag<ChangelingRuleComponent>(session, "Changeling");
                break;
        }
    }
}
