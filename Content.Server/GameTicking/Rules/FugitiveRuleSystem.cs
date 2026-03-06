// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

using Content.Server.Antag;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.GridPreloader;
using Content.Server.Antag.Components;
using Content.Server.Inventory;
using Content.Server.Objectives;
using Content.Server.Objectives.Systems;
using Content.Server.Pinpointer;
using Content.Server.Roles;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Server.Objectives.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Pinpointer;
using System.Linq;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking.Rules;

public sealed class FugitiveRuleSystem : GameRuleSystem<FugitiveRuleComponent>
{
    private static readonly HashSet<string> MaintenanceSpawnerPrototypes = new()
    {
        "MaintenanceFluffSpawner",
        "MaintenanceToolSpawner",
        "MaintenanceWeaponSpawner",
        "MaintenancePlantSpawner",
        "MaintenanceInsulsSpawner",
    };

    private const string FugitiveSurviveObjective = "FugitiveSurviveObjective";
    private const string FugitiveHunterCaptureObjective = "FugitiveHunterCaptureObjective";

    [Dependency] private readonly GridPreloaderSystem _gridPreloader = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly ObjectivesSystem _objectives = default!;
    [Dependency] private readonly PinpointerSystem _pinpointer = default!;
    [Dependency] private readonly SharedRoleSystem _role = default!;
    [Dependency] private readonly TargetObjectiveSystem _targetObjective = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FugitiveRuleComponent, AfterAntagEntitySelectedEvent>(OnAfterAntagSelected);
        SubscribeLocalEvent<FugitiveRoleComponent, GetBriefingEvent>(OnFugitiveBriefing);
        SubscribeLocalEvent<FugitiveHunterRoleComponent, GetBriefingEvent>(OnFugitiveHunterBriefing);
    }

    protected override void Added(EntityUid uid, FugitiveRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        if (component.HunterShuttles.Count == 0)
            return;

        EntityUid? shuttle = null;
        var startIndex = RobustRandom.Next(component.HunterShuttles.Count);
        for (var offset = 0; offset < component.HunterShuttles.Count; offset++)
        {
            var index = (startIndex + offset) % component.HunterShuttles.Count;
            var shuttleProto = component.HunterShuttles[index];
            if (!_gridPreloader.TryGetPreloadedGrid(shuttleProto, out var loadedShuttle) || loadedShuttle is not { } loaded)
                continue;

            shuttle = loaded;
            break;
        }

        if (shuttle is not { } hunterShuttle)
        {
            // Fallback: always load a small cargo shuttle map so the rule can still function
            // even if preloaded grids are unavailable.
            _map.CreateMap(out var mapId);
            var opts = DeserializationOptions.Default with { InitializeMaps = true };
            if (!_mapLoader.TryLoadGrid(mapId, new ResPath("/Maps/Shuttles/ShuttleEvent/fugitive_ship.yml"), out var loadedGrid, opts))
            {
                Log.Error($"Failed to load any fugitive hunter shuttle for rule {ToPrettyString(uid)}.");
                ForceEndSelf(uid, gameRule);
                return;
            }

            hunterShuttle = loadedGrid.Value.Owner;
            component.HunterShuttleGrids.Add(hunterShuttle);
            var fallbackEv = new RuleLoadedGridsEvent(mapId, new List<EntityUid> { hunterShuttle });
            RaiseLocalEvent(uid, ref fallbackEv);
            return;
        }

        var mapUid = _map.CreateMap(out var preloadedMapId, runMapInit: false);
        _xform.SetParent(hunterShuttle, mapUid);
        _map.InitializeMap(mapUid);

        component.HunterShuttleGrids.Add(hunterShuttle);

        var loadedEv = new RuleLoadedGridsEvent(preloadedMapId, new List<EntityUid> { hunterShuttle });
        RaiseLocalEvent(uid, ref loadedEv);
    }

    private void OnAfterAntagSelected(Entity<FugitiveRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        if (args.Def.PrefRoles.Contains("Fugitive"))
        {
            if (TryFindMaintenanceCoordinates(out var coords) || TryFindRandomTile(out _, out _, out _, out coords))
                _xform.SetCoordinates(args.EntityUid, coords);

            if (HasComp<GhostRoleAntagSpawnerComponent>(args.EntityUid))
                return;

            EnsureFugitiveObjective(args.EntityUid);
            UpdateHunterTrackers(ent.Comp);
            return;
        }

        if (!args.Def.PrefRoles.Contains("FugitiveHunter"))
            return;

        if (HasComp<GhostRoleAntagSpawnerComponent>(args.EntityUid))
            return;

        ConfigureHunterTrackers(args.EntityUid, ent.Comp);
        EnsureHunterObjectives(args.EntityUid);
    }

    private bool TryFindMaintenanceCoordinates(out EntityCoordinates coords)
    {
        coords = EntityCoordinates.Invalid;

        if (!TryGetRandomStation(out var station))
            return false;

        var stationMap = Transform(station.Value).MapID;
        var candidates = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out var xform, out var metadata))
        {
            if (xform.MapID != stationMap)
                continue;

            if (metadata.EntityPrototype?.ID is not { } protoId || !MaintenanceSpawnerPrototypes.Contains(protoId))
                continue;

            candidates.Add(xform.Coordinates);
        }

        if (candidates.Count == 0)
            return false;

        coords = candidates[RobustRandom.Next(candidates.Count)];
        return true;
    }

    private void EnsureFugitiveObjective(EntityUid fugitive)
    {
        if (!_mind.TryGetMind(fugitive, out var mindId, out var mind))
            return;

        foreach (var objective in mind.Objectives)
        {
            if (MetaData(objective).EntityPrototype?.ID == FugitiveSurviveObjective)
                return;
        }

        _mind.TryAddObjective(mindId, mind, FugitiveSurviveObjective);
    }

    private void EnsureHunterObjectives(EntityUid hunter)
    {
        if (!_mind.TryGetMind(hunter, out var hunterMindId, out var hunterMind))
            return;

        var fugitives = GetMindsWithRole<FugitiveRoleComponent>()
            .Select(m => m.Comp.OwnedEntity)
            .Where(uid => uid != null)
            .Select(uid => uid!.Value)
            .ToList();

        if (fugitives.Count == 0)
            return;

        foreach (var fugitive in fugitives)
        {
            if (!_mind.TryGetMind(fugitive, out var fugitiveMindId, out _))
                continue;

            if (HasCaptureObjective(hunterMind, fugitiveMindId))
                continue;

            if (_objectives.TryCreateObjective(hunterMindId, hunterMind, FugitiveHunterCaptureObjective) is not { } objective)
                continue;

            _targetObjective.SetTarget(objective, fugitiveMindId);
            _mind.AddObjective(hunterMindId, hunterMind, objective);
        }
    }

    private bool HasCaptureObjective(MindComponent hunterMind, EntityUid fugitiveMindId)
    {
        foreach (var objective in hunterMind.Objectives)
        {
            if (MetaData(objective).EntityPrototype?.ID != FugitiveHunterCaptureObjective)
                continue;

            if (!TryComp<TargetObjectiveComponent>(objective, out var target) || target.Target != fugitiveMindId)
                continue;

            return true;
        }

        return false;
    }

    private void OnFugitiveBriefing(Entity<FugitiveRoleComponent> ent, ref GetBriefingEvent args)
    {
        var name = args.Mind.Comp.CharacterName ?? Name(args.Mind.Owner);
        args.Append(Loc.GetString("fugitive-role-briefing", ("name", name)));
    }

    private void OnFugitiveHunterBriefing(Entity<FugitiveHunterRoleComponent> ent, ref GetBriefingEvent args)
    {
        var fugitives = GetMindsWithRole<FugitiveRoleComponent>();
        if (fugitives.Count == 0)
        {
            args.Append(Loc.GetString("fugitive-hunter-role-briefing-no-targets"));
            return;
        }

        var names = string.Join(", ", fugitives.Select(f => f.Comp.CharacterName ?? "Unknown"));
        args.Append(Loc.GetString("fugitive-hunter-role-briefing", ("fugitives", names)));
    }

    private void UpdateHunterTrackers(FugitiveRuleComponent rule)
    {
        var hunters = GetMindsWithRole<FugitiveHunterRoleComponent>();
        foreach (var hunterMind in hunters)
        {
            if (hunterMind.Comp.OwnedEntity is not { } hunter)
                continue;

            ConfigureHunterTrackers(hunter, rule);
        }
    }

    private void ConfigureHunterTrackers(EntityUid hunter, FugitiveRuleComponent rule)
    {
        var fugitiveTarget = GetMindsWithRole<FugitiveRoleComponent>()
            .Select(m => m.Comp.OwnedEntity)
            .FirstOrDefault(uid => uid != null);

        foreach (var item in _inventory.GetHandOrInventoryEntities(hunter))
        {
            if (!TryComp<PinpointerComponent>(item, out var pinpointer))
                continue;

            if (HasComp<FugitiveShipPinpointerComponent>(item))
            {
                var ship = rule.HunterShuttleGrids.FirstOrDefault();
                _pinpointer.SetTargetWithCustomName(item, ship, Loc.GetString("fugitive-hunter-ship-pinpointer-target"), pinpointer);
                _pinpointer.SetActive(item, true, pinpointer);
                continue;
            }

            if (!HasComp<FugitiveBountyPinpointerComponent>(item))
                continue;

            var targetName = fugitiveTarget == null
                ? Loc.GetString("fugitive-hunter-bounty-pinpointer-no-target")
                : Name(fugitiveTarget.Value);

            _pinpointer.SetTargetWithCustomName(item, fugitiveTarget, targetName, pinpointer);
            _pinpointer.SetActive(item, true, pinpointer);
        }
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
