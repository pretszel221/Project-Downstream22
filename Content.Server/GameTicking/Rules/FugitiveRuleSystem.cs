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
using Content.Shared.Zombies;
using Content.Server.Zombies;
using System;
using System.Linq;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server.GameTicking.Rules;

public sealed class FugitiveRuleSystem : GameRuleSystem<FugitiveRuleComponent>
{
    private readonly record struct FugitiveRoundEndStats(
        int TotalFugitives,
        int AliveFugitives,
        int DeadFugitives,
        int CapturedFugitives,
        int CapturedAliveFugitives,
        int CapturedDeadFugitives,
        int TotalHunters,
        int AliveHunters,
        int DeadHunters,
        bool AllHuntersDead);

    private static readonly HashSet<string> MaintenanceSpawnerPrototypes = new()
    {
        "MaintenanceFluffSpawner",
        "MaintenanceToolSpawner",
        "MaintenanceWeaponSpawner",
        "MaintenancePlantSpawner",
        "MaintenanceInsulsSpawner",
    };

    private static readonly string[] FugitiveTrackedSlots = new[] { "jumpsuit", "outerClothing", "id" };

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
        ent.Comp.HunterShuttleGrids.UnionWith(args.Grids);
    }

    protected override void Started(EntityUid uid, FugitiveRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
	{
		base.Started(uid, component, gameRule, args);

		var query = EntityQueryEnumerator<FugitiveRuleComponent, GameRuleComponent>();
		while (query.MoveNext(out var otherUid, out _, out _))
		{
			if (otherUid == uid)
				continue;

			GameTicker.EndGameRule(uid);
			return;
		}
	}
	
    private void OnAfterAntagSelected(Entity<FugitiveRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        if (args.Def.PrefRoles.Contains(ent.Comp.FugitivePrefRole))
        {
            if (TryFindMaintenanceCoordinates(out var coords) || TryFindRandomTile(out _, out _, out _, out coords))
                _xform.SetCoordinates(args.EntityUid, coords);

            RegisterFugitive(ent.Comp, args.EntityUid);

            EnsureFugitiveObjective(args.EntityUid, ent.Comp);
            if (ent.Comp.FugitivesAreInitialInfected)
                EnsureInitialInfectedComponents(args.EntityUid, ent.Comp);
            UpdateHunterTrackers(ent.Comp);
            return;
        }

        if (!args.Def.PrefRoles.Contains(ent.Comp.HunterPrefRole))
            return;

        ConfigureHunterTrackers(args.EntityUid, ent.Comp);
        EnsureHunterObjectives(args.EntityUid, ent.Comp);
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

    private void EnsureFugitiveObjective(EntityUid fugitive, FugitiveRuleComponent rule)
    {
        if (!_mind.TryGetMind(fugitive, out var mindId, out var mind))
            return;

        foreach (var objective in mind.Objectives)
        {
            if (MetaData(objective).EntityPrototype?.ID == rule.FugitiveObjectivePrototype)
                return;
        }

        _mind.TryAddObjective(mindId, mind, rule.FugitiveObjectivePrototype);
    }

    private void EnsureHunterObjectives(EntityUid hunter, FugitiveRuleComponent rule)
    {
        if (!_mind.TryGetMind(hunter, out var hunterMindId, out var hunterMind))
            return;

        EnsureHunterCaptureQuotaObjective(hunterMindId, hunterMind, rule);
    }

    private void RegisterFugitive(FugitiveRuleComponent rule, EntityUid fugitive)
    {
        if (!_mind.TryGetMind(fugitive, out var mindId, out _))
            return;

        if (!rule.FugitiveMinds.Add(mindId))
            return;

        rule.TotalFugitives = rule.FugitiveMinds.Count;
    }

    private void EnsureHunterCaptureQuotaObjective(EntityUid hunterMindId, MindComponent hunterMind, FugitiveRuleComponent? rule)
    {
        var objectives = rule?.HunterObjectivePrototypes ?? new List<string> { "FugitiveHunterCaptureQuotaObjective" };

        foreach (var objective in objectives)
        {
            var exists = false;
            foreach (var assigned in hunterMind.Objectives)
            {
                if (MetaData(assigned).EntityPrototype?.ID != objective)
                    continue;

                exists = true;
                break;
            }

            if (!exists)
                _mind.TryAddObjective(hunterMindId, hunterMind, objective);
        }
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
        var rule = GetCurrentRule();
        args.Append(Loc.GetString(rule?.HunterBriefingPrefLoc ?? "fugitive-hunter-role-briefing-preference"));

        var fugitives = GetMindsWithRole<FugitiveRoleComponent>();
        if (fugitives.Count == 0)
        {
            args.Append(Loc.GetString(rule?.HunterBriefingNoTargetsLoc ?? "fugitive-hunter-role-briefing-no-targets"));
            return;
        }

        var names = string.Join(", ", fugitives.Select(f => f.Comp.CharacterName ?? "Unknown"));
        args.Append(Loc.GetString(rule?.HunterBriefingTargetsLoc ?? "fugitive-hunter-role-briefing", ("fugitives", names)));
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
                _pinpointer.SetTargetWithCustomName(item, ship, Loc.GetString(rule.ShipPinpointerTargetLoc), pinpointer);
                _pinpointer.SetActive(item, true, pinpointer);
                continue;
            }

            if (!TryComp<FugitiveBountyPinpointerComponent>(item, out var bounty))
                continue;

            ResetBountyTracker(item, bounty, pinpointer, rule.BountyNoTargetLoc);
        }
    }


    private void ResetBountyTracker(EntityUid tracker,
        FugitiveBountyPinpointerComponent bounty,
        PinpointerComponent pinpointer,
        string noTargetLoc)
    {
        bounty.ActiveTracking = false;
        bounty.TimeRemaining = bounty.CooldownSeconds;
        _pinpointer.SetTargetWithCustomName(tracker, null, Loc.GetString(noTargetLoc), pinpointer);
        _pinpointer.SetActive(tracker, false, pinpointer);
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

        var trackedClothingTargets = GetTrackedClothingTargets();
        var rule = GetCurrentRule();

        var query = EntityQueryEnumerator<FugitiveBountyPinpointerComponent, PinpointerComponent>();
        while (query.MoveNext(out var uid, out var bounty, out var pinpointer))
        {
            bounty.TimeRemaining -= frameTime;
            if (bounty.TimeRemaining > 0f)
                continue;

            if (bounty.ActiveTracking)
            {
                ResetBountyTracker(uid, bounty, pinpointer, rule?.BountyNoTargetLoc ?? "fugitive-hunter-bounty-pinpointer-no-target");
                continue;
            }

            bounty.ActiveTracking = true;
            bounty.TimeRemaining = bounty.ActiveSeconds;

            EntityUid? suitTarget = trackedClothingTargets.Count == 0
                ? null
                : trackedClothingTargets[RobustRandom.Next(trackedClothingTargets.Count)];

            var targetName = suitTarget == null
                ? Loc.GetString(rule?.BountyNoTargetLoc ?? "fugitive-hunter-bounty-pinpointer-no-target")
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

        var (fugitives, hunters) = GetFugitiveAndHunterMinds();
        var stats = ComputeRoundEndStats(component, fugitives, hunters);

        var outcome = component.FugitivesAreInitialInfected
            ? GetCburnOutcome(stats.TotalFugitives, stats.CapturedFugitives, stats.TotalHunters, stats.AliveHunters)
            : GetOutcome(stats.TotalFugitives,
                stats.AliveFugitives,
                stats.CapturedFugitives,
                stats.CapturedAliveFugitives,
                stats.AllHuntersDead);
        var outcomePrefix = component.FugitivesAreInitialInfected ? "cburn-round-end" : "fugitive-round-end";
        args.AddLine(Loc.GetString($"{outcomePrefix}-{outcome}"));

        args.AddLine(Loc.GetString("fugitive-round-end-counts",
            ("fugitives", stats.TotalFugitives),
            ("alive", stats.AliveFugitives),
            ("dead", stats.DeadFugitives),
            ("captured", stats.CapturedFugitives),
            ("capturedAlive", stats.CapturedAliveFugitives),
            ("capturedDead", stats.CapturedDeadFugitives),
            ("hunters", stats.TotalHunters),
            ("huntersAlive", stats.AliveHunters),
            ("huntersDead", stats.DeadHunters)));

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

    private static string GetCburnOutcome(int totalFugitives, int capturedFugitives, int totalHunters, int aliveHunters)
    {
        if (totalFugitives > 0 && capturedFugitives >= totalFugitives)
            return "major-cburn-victory";

        if (totalHunters > 0 && aliveHunters == 0)
            return "bad-ending";

        return "neutral-outcome";
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

    private FugitiveRoundEndStats ComputeRoundEndStats(FugitiveRuleComponent component,
        IReadOnlyList<Entity<MindComponent>> fugitives,
        IReadOnlyList<Entity<MindComponent>> hunters)
    {
        var totalFugitives = Math.Max(component.TotalFugitives, fugitives.Count);
        var aliveFugitives = CountAliveOwnedEntities(fugitives);

        var capturedFugitives = Math.Min(component.CapturedFugitives, totalFugitives);
        var deadFugitives = Math.Max(0, totalFugitives - aliveFugitives);
        var capturedAliveFugitives = Math.Max(0, capturedFugitives - deadFugitives);
        capturedAliveFugitives = Math.Min(capturedAliveFugitives, aliveFugitives);
        var capturedDeadFugitives = Math.Max(0, capturedFugitives - capturedAliveFugitives);

        var totalHunters = hunters.Count;
        var aliveHunters = CountAliveOwnedEntities(hunters);
        var deadHunters = Math.Max(0, totalHunters - aliveHunters);
        var allHuntersDead = totalHunters > 0 && aliveHunters == 0;

        return new FugitiveRoundEndStats(
            totalFugitives,
            aliveFugitives,
            deadFugitives,
            capturedFugitives,
            capturedAliveFugitives,
            capturedDeadFugitives,
            totalHunters,
            aliveHunters,
            deadHunters,
            allHuntersDead);
    }

    private int CountAliveOwnedEntities(IReadOnlyList<Entity<MindComponent>> minds)
    {
        var aliveCount = 0;

        foreach (var mind in minds)
        {
            if (mind.Comp.OwnedEntity is not { } entity)
                continue;

            if (_mobState.IsAlive(entity))
                aliveCount++;
        }

        return aliveCount;
    }

    private List<EntityUid> GetTrackedClothingTargets()
    {
        var targets = new List<EntityUid>();
        var fugitives = GetMindsWithRole<FugitiveRoleComponent>();

        foreach (var fugitiveMind in fugitives)
        {
            if (fugitiveMind.Comp.OwnedEntity is not { } fugitive)
                continue;

            foreach (var slot in FugitiveTrackedSlots)
            {
                if (!_inventory.TryGetSlotEntity(fugitive, slot, out var clothing) || clothing == null)
                    continue;

                targets.Add(clothing.Value);
                break;
            }
        }

        return targets;
    }

    private (List<Entity<MindComponent>> Fugitives, List<Entity<MindComponent>> Hunters) GetFugitiveAndHunterMinds()
    {
        var fugitives = new List<Entity<MindComponent>>();
        var hunters = new List<Entity<MindComponent>>();

        var query = EntityQueryEnumerator<MindComponent>();
        while (query.MoveNext(out var uid, out var mind))
        {
            var mindEnt = (uid, mind);

            if (_role.MindHasRole<FugitiveRoleComponent>(mindEnt, out _))
                fugitives.Add(mindEnt);

            if (_role.MindHasRole<FugitiveHunterRoleComponent>(mindEnt, out _))
                hunters.Add(mindEnt);
        }

        return (fugitives, hunters);
    }

    private FugitiveRuleComponent? GetCurrentRule()
    {
        var query = EntityQueryEnumerator<FugitiveRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out _, out var rule, out _))
        {
            return rule;
        }

        return null;
    }

    private void EnsureInitialInfectedComponents(EntityUid fugitive, FugitiveRuleComponent rule)
    {
        var pending = EnsureComp<PendingZombieComponent>(fugitive);
        var grace = TimeSpan.FromSeconds(Math.Max(0f, rule.InitialInfectedGraceSeconds));
        pending.MinInitialInfectedGrace = grace;
        pending.MaxInitialInfectedGrace = grace;

        EnsureComp<ZombifyOnDeathComponent>(fugitive);
        EnsureComp<IncurableZombieComponent>(fugitive);
        EnsureComp<InitialInfectedComponent>(fugitive);
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
