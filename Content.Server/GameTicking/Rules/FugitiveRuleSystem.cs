// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

using Content.Server.Antag;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Inventory;
using Content.Server.Objectives.Systems;
using Content.Server.Pinpointer;
using Content.Server.Roles;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Pinpointer;
using Content.Shared.Examine;
using System;
using System.Linq;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

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
    private const string FugitiveHunterCaptureQuotaObjective = "FugitiveHunterCaptureQuotaObjective";

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly PinpointerSystem _pinpointer = default!;
    [Dependency] private readonly SharedRoleSystem _role = default!;
    [Dependency] private readonly TransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FugitiveRuleComponent, AfterAntagEntitySelectedEvent>(OnAfterAntagSelected);
        SubscribeLocalEvent<FugitiveRoleComponent, GetBriefingEvent>(OnFugitiveBriefing);
        SubscribeLocalEvent<FugitiveHunterRoleComponent, GetBriefingEvent>(OnFugitiveHunterBriefing);
        SubscribeLocalEvent<FugitiveBountyPinpointerComponent, ExaminedEvent>(OnBountyTrackerExamined);
        SubscribeLocalEvent<FugitiveCapturedEvent>(OnFugitiveCaptured);
        SubscribeLocalEvent<FugitiveRuleComponent, RuleLoadedGridsEvent>(OnRuleLoadedGrids);
    }

    private void OnRuleLoadedGrids(Entity<FugitiveRuleComponent> ent, ref RuleLoadedGridsEvent args)
    {
        ent.Comp.HunterShuttleGrids.Clear();
        foreach (var grid in args.Grids)
        {
            ent.Comp.HunterShuttleGrids.Add(grid);
        }
    }

    private void OnAfterAntagSelected(Entity<FugitiveRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        if (args.Def.PrefRoles.Contains("Fugitive"))
        {
            if (TryFindMaintenanceCoordinates(out var coords) || TryFindRandomTile(out _, out _, out _, out coords))
                _xform.SetCoordinates(args.EntityUid, coords);

            RegisterFugitive(ent.Comp, args.EntityUid);

            EnsureFugitiveObjective(args.EntityUid);
            UpdateHunterTrackers(ent.Comp);
            return;
        }

        if (!args.Def.PrefRoles.Contains("FugitiveHunter"))
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

        EnsureHunterCaptureQuotaObjective(hunterMindId, hunterMind);
    }

    private void RegisterFugitive(FugitiveRuleComponent rule, EntityUid fugitive)
    {
        if (!_mind.TryGetMind(fugitive, out var mindId, out _))
            return;

        if (!rule.FugitiveMinds.Add(mindId))
            return;

        rule.TotalFugitives = rule.FugitiveMinds.Count;
    }

    private void EnsureHunterCaptureQuotaObjective(EntityUid hunterMindId, MindComponent hunterMind)
    {
        foreach (var objective in hunterMind.Objectives)
        {
            if (MetaData(objective).EntityPrototype?.ID == FugitiveHunterCaptureQuotaObjective)
                return;
        }

        _mind.TryAddObjective(hunterMindId, hunterMind, FugitiveHunterCaptureQuotaObjective);
    }

    private void OnFugitiveCaptured(FugitiveCapturedEvent args)
    {
        var query = EntityQueryEnumerator<FugitiveRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out _, out var rule, out _))
        {
            if (args.FugitiveMindId is { } mindId)
                rule.CapturedFugitiveMinds.Add(mindId);

            rule.TotalFugitives = Math.Max(rule.TotalFugitives, rule.FugitiveMinds.Count);
            rule.CapturedFugitives = Math.Min(rule.CapturedFugitives + 1, rule.TotalFugitives);
            break;
        }
    }

    private void OnFugitiveBriefing(Entity<FugitiveRoleComponent> ent, ref GetBriefingEvent args)
    {
        var name = args.Mind.Comp.CharacterName ?? Name(args.Mind.Owner);
        args.Append(Loc.GetString("fugitive-role-briefing", ("name", name)));
    }

    private void OnFugitiveHunterBriefing(Entity<FugitiveHunterRoleComponent> ent, ref GetBriefingEvent args)
    {
        args.Append(Loc.GetString("fugitive-hunter-role-briefing-preference"));

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

            if (!TryComp<FugitiveBountyPinpointerComponent>(item, out var bounty))
                continue;

            bounty.ActiveTracking = false;
            bounty.TimeRemaining = bounty.CooldownSeconds;
            _pinpointer.SetTargetWithCustomName(item, null, Loc.GetString("fugitive-hunter-bounty-pinpointer-no-target"), pinpointer);
            _pinpointer.SetActive(item, false, pinpointer);
        }
    }

    private EntityUid? GetRandomFugitiveTrackedClothing()
    {
        var fugitives = GetMindsWithRole<FugitiveRoleComponent>()
            .Where(m => m.Comp.OwnedEntity != null)
            .ToList();

        if (fugitives.Count == 0)
            return null;

        var fugitive = fugitives[RobustRandom.Next(fugitives.Count)].Comp.OwnedEntity!.Value;
        foreach (var slot in new[] { "jumpsuit", "outerClothing", "id" })
        {
            if (_inventory.TryGetSlotEntity(fugitive, slot, out var clothing) && clothing != null)
                return clothing.Value;
        }

        return null;
    }

    private void OnBountyTrackerExamined(Entity<FugitiveBountyPinpointerComponent> ent, ref ExaminedEvent args)
    {
        var seconds = (int) Math.Ceiling(Math.Max(0f, ent.Comp.TimeRemaining));
        var state = ent.Comp.ActiveTracking
            ? Loc.GetString("fugitive-hunter-bounty-pinpointer-state-active")
            : Loc.GetString("fugitive-hunter-bounty-pinpointer-state-cooldown");

        args.PushMarkup(Loc.GetString("fugitive-hunter-bounty-pinpointer-countdown",
            ("state", state),
            ("seconds", seconds)));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FugitiveBountyPinpointerComponent, PinpointerComponent>();
        while (query.MoveNext(out var uid, out var bounty, out var pinpointer))
        {
            bounty.TimeRemaining -= frameTime;
            if (bounty.TimeRemaining > 0f)
                continue;

            if (bounty.ActiveTracking)
            {
                bounty.ActiveTracking = false;
                bounty.TimeRemaining = bounty.CooldownSeconds;
                _pinpointer.SetTargetWithCustomName(uid, null, Loc.GetString("fugitive-hunter-bounty-pinpointer-no-target"), pinpointer);
                _pinpointer.SetActive(uid, false, pinpointer);
                continue;
            }

            bounty.ActiveTracking = true;
            bounty.TimeRemaining = bounty.ActiveSeconds;

            var suitTarget = GetRandomFugitiveTrackedClothing();
            var targetName = suitTarget == null
                ? Loc.GetString("fugitive-hunter-bounty-pinpointer-no-target")
                : Loc.GetString("fugitive-hunter-bounty-pinpointer-clothing-target", ("clothing", Name(suitTarget.Value)));

            _pinpointer.SetTargetWithCustomName(uid, suitTarget, targetName, pinpointer);
            _pinpointer.SetActive(uid, suitTarget != null, pinpointer);
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

        var totalFugitives = Math.Max(component.TotalFugitives, fugitives.Count);
        var aliveFugitives = 0;

        foreach (var mind in fugitives)
        {
            var entity = mind.Comp.OwnedEntity;
            if (entity != null && _mobState.IsAlive(entity.Value))
                aliveFugitives++;
        }

        var capturedFugitives = Math.Min(component.CapturedFugitives, totalFugitives);
        var deadFugitives = Math.Max(0, totalFugitives - aliveFugitives);
        var capturedAliveFugitives = Math.Max(0, capturedFugitives - deadFugitives);
        capturedAliveFugitives = Math.Min(capturedAliveFugitives, aliveFugitives);
        var capturedDeadFugitives = Math.Max(0, capturedFugitives - capturedAliveFugitives);

        var totalHunters = hunters.Count;
        var aliveHunters = 0;
        foreach (var mind in hunters)
        {
            var entity = mind.Comp.OwnedEntity;
            if (entity != null && _mobState.IsAlive(entity.Value))
                aliveHunters++;
        }

        var deadHunters = Math.Max(0, totalHunters - aliveHunters);
        var allHuntersDead = totalHunters > 0 && aliveHunters == 0;

        var outcome = GetOutcome(totalFugitives, aliveFugitives, capturedFugitives, capturedAliveFugitives, allHuntersDead);
        args.AddLine(Loc.GetString($"fugitive-round-end-{outcome}"));

        args.AddLine(Loc.GetString("fugitive-round-end-counts",
            ("fugitives", totalFugitives),
            ("alive", aliveFugitives),
            ("dead", deadFugitives),
            ("captured", capturedFugitives),
            ("capturedAlive", capturedAliveFugitives),
            ("capturedDead", capturedDeadFugitives),
            ("hunters", totalHunters),
            ("huntersAlive", aliveHunters),
            ("huntersDead", deadHunters)));

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
