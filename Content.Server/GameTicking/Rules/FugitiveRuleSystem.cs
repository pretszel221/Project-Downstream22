// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

using Content.Server.Antag;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.GridPreloader;
using Content.Server.Roles;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Robust.Server.GameObjects;

namespace Content.Server.GameTicking.Rules;

public sealed class FugitiveRuleSystem : GameRuleSystem<FugitiveRuleComponent>
{
    [Dependency] private readonly GridPreloaderSystem _gridPreloader = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _role = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FugitiveRuleComponent, GameRuleAddedEvent>(OnRuleAdded);
        SubscribeLocalEvent<FugitiveRuleComponent, AfterAntagEntitySelectedEvent>(OnAfterAntagSelected);
    }

    private void OnRuleAdded(Entity<FugitiveRuleComponent> ent, ref GameRuleAddedEvent args)
    {
        if (ent.Comp.HunterShuttles.Count == 0)
            return;

        var shuttleProto = ent.Comp.HunterShuttles[RobustRandom.Next(ent.Comp.HunterShuttles.Count)];
        if (!_gridPreloader.TryGetPreloadedGrid(shuttleProto, out var loadedShuttle) || loadedShuttle is not { } shuttle)
            return;

        var mapUid = _map.CreateMap(out var mapId, runMapInit: false);
        _xform.SetParent(shuttle, mapUid);
        _map.InitializeMap(mapUid);

        ent.Comp.HunterShuttleGrids.Add(shuttle);

        var loadedEv = new RuleLoadedGridsEvent(mapId, new List<EntityUid> { shuttle });
        RaiseLocalEvent(ent.Owner, ref loadedEv);
    }

    private void OnAfterAntagSelected(Entity<FugitiveRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        if (!args.Def.PrefRoles.Contains("Fugitive"))
            return;

        if (!TryFindRandomTile(out _, out _, out _, out var coords))
            return;

        _xform.SetCoordinates(args.EntityUid, coords);
    }

    protected override void AppendRoundEndText(EntityUid uid,
        FugitiveRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        var fugitives = GetMindsWithRole<FugitiveRoleComponent>();
        var hunters = GetMindsWithRole<FugitiveHunterRoleComponent>();

        var totalFugitives = fugitives.Count;
        var aliveFugitives = 0;
        var capturedFugitives = 0;
        var capturedAliveFugitives = 0;

        foreach (var mind in fugitives)
        {
            var entity = mind.Comp.OwnedEntity;
            if (entity == null)
                continue;

            var alive = _mobState.IsAlive(entity.Value);
            if (alive)
                aliveFugitives++;

            if (IsOnHunterShuttle(entity.Value, component))
            {
                capturedFugitives++;
                if (alive)
                    capturedAliveFugitives++;
            }
        }

        var aliveHunters = 0;
        foreach (var mind in hunters)
        {
            var entity = mind.Comp.OwnedEntity;
            if (entity != null && _mobState.IsAlive(entity.Value))
                aliveHunters++;
        }

        var allHuntersDead = hunters.Count > 0 && aliveHunters == 0;

        var outcome = GetOutcome(totalFugitives, aliveFugitives, capturedFugitives, capturedAliveFugitives, allHuntersDead);
        args.AddLine(Loc.GetString($"fugitive-round-end-{outcome}"));

        args.AddLine(Loc.GetString("fugitive-round-end-counts",
            ("fugitives", totalFugitives),
            ("alive", aliveFugitives),
            ("captured", capturedFugitives),
            ("capturedAlive", capturedAliveFugitives),
            ("hunters", hunters.Count),
            ("huntersAlive", aliveHunters)));

        args.AddLine(Loc.GetString("fugitive-round-end-fugitives-list"));
        foreach (var mind in fugitives)
        {
            if (!_mind.TryGetSession(mind.Owner, out var session))
                continue;

            args.AddLine(Loc.GetString("fugitive-round-end-player-entry", ("name", (object)(mind.Comp.CharacterName ?? "Unknown")), ("user", session.Name)));
        }

        args.AddLine(Loc.GetString("fugitive-round-end-hunters-list"));
        foreach (var mind in hunters)
        {
            if (!_mind.TryGetSession(mind.Owner, out var session))
                continue;

            args.AddLine(Loc.GetString("fugitive-round-end-player-entry", ("name", (object)(mind.Comp.CharacterName ?? "Unknown")), ("user", session.Name)));
        }
    }

    private static string GetOutcome(int totalFugitives, int aliveFugitives, int capturedFugitives, int capturedAliveFugitives, bool allHuntersDead)
    {
        if (totalFugitives == 0)
            return "stalemate";

        if (capturedFugitives == totalFugitives && capturedAliveFugitives == totalFugitives)
            return "badass-security-victory";

        if (capturedFugitives == totalFugitives && allHuntersDead)
            return "postmortem-security-victory";

        if (capturedFugitives == totalFugitives)
            return "major-security-victory";

        if (allHuntersDead && capturedFugitives > 0)
            return "minor-security-victory";

        if (capturedFugitives > 0)
            return "security-victory";

        if (aliveFugitives == totalFugitives)
            return "major-fugitive-victory";

        if (aliveFugitives > 0)
            return "fugitive-victory";

        if (allHuntersDead)
            return "stalemate";

        return "minor-fugitive-victory";
    }

    private bool IsOnHunterShuttle(EntityUid uid, FugitiveRuleComponent comp)
    {
        var xform = Transform(uid);
        if (xform.GridUid is { } grid && comp.HunterShuttleGrids.Contains(grid))
            return true;

        var parent = xform.ParentUid;
        while (parent.IsValid() && TryComp(parent, out TransformComponent? parentXform))
        {
            if (parentXform.GridUid is { } parentGrid && comp.HunterShuttleGrids.Contains(parentGrid))
                return true;

            parent = parentXform.ParentUid;
        }

        return false;
    }

    private List<Entity<MindComponent>> GetMindsWithRole<TRole>() where TRole : IComponent
    {
        var result = new List<Entity<MindComponent>>();
        var query = EntityQueryEnumerator<MindComponent>();
        while (query.MoveNext(out var uid, out var mind))
        {
            if (!_role.MindHasRole<TRole>((uid, mind), out _))
                continue;

            result.Add((uid, mind));
        }

        return result;
    }
}
