// SPDX-FileCopyrightText: 2021 Paul Ritter <ritter.paul1@googlemail.com>
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto <6766154+Zumorica@users.noreply.github.com>
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto <gradientvera@outlook.com>
// SPDX-FileCopyrightText: 2021 metalgearsloth <comedian_vs_clown@hotmail.com>
// SPDX-FileCopyrightText: 2021 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2021 pointer-to-null <91910481+pointer-to-null@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Acruid <shatter66@gmail.com>
// SPDX-FileCopyrightText: 2022 Chief-Engineer <119664036+Chief-Engineer@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Rane <60792108+Elijahrane@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 keronshb <54602815+keronshb@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 wrexbe <81056464+wrexbe@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <drsmugleaf@gmail.com>
// SPDX-FileCopyrightText: 2023 Jezithyr <jezithyr@gmail.com>
// SPDX-FileCopyrightText: 2023 Kara <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2023 Pieter-Jan Briers <pieterjan.briers@gmail.com>
// SPDX-FileCopyrightText: 2023 deltanedas <39013340+deltanedas@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Aiden <aiden@djkraz.com>
// SPDX-FileCopyrightText: 2024 Aviu00 <93730715+Aviu00@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 John Space <bigdumb421@gmail.com>
// SPDX-FileCopyrightText: 2024 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 PJBot <pieterjan.briers+bot@gmail.com>
// SPDX-FileCopyrightText: 2024 Tadeo <td12233a@gmail.com>
// SPDX-FileCopyrightText: 2024 Tayrtahn <tayrtahn@gmail.com>
// SPDX-FileCopyrightText: 2024 whateverusername0 <whateveremail>
// SPDX-FileCopyrightText: 2025 Drywink <hugogrethen@gmail.com>
// SPDX-FileCopyrightText: 2025 Princess Cheeseballs <66055347+princess-cheeseballs@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Princess Cheeseballs <66055347+pronana@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Princess-Cheeseballs <https://github.com/Princess-Cheeseballs>
// SPDX-FileCopyrightText: 2025 Tay <td12233a@gmail.com>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Input;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared.Physics;
using Content.Shared.StatusEffect;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Containers;
using Content.Shared._White.Standing;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.Jittering;
using Robust.Shared.Timing;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared.Stunnable;

public abstract partial class SharedStunSystem : EntitySystem
{
    private readonly Dictionary<EntityUid, TimeSpan> _nextToggleKnockdownAt = new();
    private readonly Dictionary<EntityUid, TimeSpan> _nextStandAttemptAt = new();
    private static readonly TimeSpan AutoStandRetryDelay = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan ToggleKnockdownCooldown = TimeSpan.FromSeconds(0.8);
    private static readonly TimeSpan ManualStandAttemptCooldown = TimeSpan.FromSeconds(0.8);

    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedBroadphaseSystem _broadphase = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] protected readonly SharedAppearanceSystem Appearance = default!;
    [Dependency] private readonly EntityWhitelistSystem _entityWhitelist = default!;
    [Dependency] private readonly StandingStateSystem _standingState = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffect = default!;
    [Dependency] private readonly SharedStutteringSystem _stutter = default!; // goob edit
    [Dependency] private readonly SharedJitteringSystem _jitter = default!; // goob edit
    [Dependency] private readonly ClothingModifyStunTimeSystem _modify = default!; // goob edit
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KnockedDownComponent, ComponentInit>(OnKnockInit);
        SubscribeLocalEvent<KnockedDownComponent, ComponentShutdown>(OnKnockShutdown);
        SubscribeLocalEvent<KnockedDownComponent, StandAttemptEvent>(OnStandAttempt);
        SubscribeLocalEvent<KnockedDownComponent, RefreshMovementSpeedModifiersEvent>(OnKnockedRefreshSpeed);
        SubscribeLocalEvent<CrawlerComponent, KnockedDownRefreshEvent>(OnCrawlerKnockedRefresh);
        SubscribeLocalEvent<CrawlerComponent, global::Content.Shared.Damage.DamageChangedEvent>(OnCrawlerDamaged);
        SubscribeLocalEvent<KnockedDownComponent, TryStandDoAfterEvent>(OnStandDoAfter);
        SubscribeLocalEvent<KnockedDownComponent, DidEquipHandEvent>(OnHandEquippedWhileKnocked);
        SubscribeLocalEvent<KnockedDownComponent, DidUnequipHandEvent>(OnHandUnequippedWhileKnocked);
        SubscribeLocalEvent<KnockedDownComponent, HandCountChangedEvent>(OnHandCountChangedWhileKnocked);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.ToggleKnockdown, InputCmdHandler.FromDelegate(HandleToggleKnockdown, handle: false))
            .Register<SharedStunSystem>();

        SubscribeLocalEvent<SlowedDownComponent, ComponentInit>(OnSlowInit);
        SubscribeLocalEvent<SlowedDownComponent, ComponentShutdown>(OnSlowRemove);

        SubscribeLocalEvent<StunnedComponent, ComponentStartup>(UpdateCanMove);
        SubscribeLocalEvent<StunnedComponent, ComponentShutdown>(OnStunShutdown);

        SubscribeLocalEvent<StunOnContactComponent, ComponentStartup>(OnStunOnContactStartup);
        SubscribeLocalEvent<StunOnContactComponent, StartCollideEvent>(OnStunOnContactCollide);

        // helping people up if they're knocked down
        SubscribeLocalEvent<SlowedDownComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);

        SubscribeLocalEvent<KnockedDownComponent, TileFrictionEvent>(OnKnockedTileFriction);

        // Attempt event subscriptions.
        SubscribeLocalEvent<StunnedComponent, ChangeDirectionAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<StunnedComponent, UpdateCanMoveEvent>(OnMoveAttempt);
        SubscribeLocalEvent<StunnedComponent, InteractionAttemptEvent>(OnAttemptInteract);
        SubscribeLocalEvent<StunnedComponent, UseAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<StunnedComponent, ThrowAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<StunnedComponent, DropAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<StunnedComponent, AttackAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<StunnedComponent, PickupAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<StunnedComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<StunnedComponent, IsUnequippingAttemptEvent>(OnUnequipAttempt);
        SubscribeLocalEvent<MobStateComponent, MobStateChangedEvent>(OnMobStateChanged);

        // Stun Appearance Data
        InitializeAppearance();
    }

    private void OnAttemptInteract(Entity<StunnedComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnMobStateChanged(EntityUid uid, MobStateComponent component, MobStateChangedEvent args)
    {
        if (!TryComp<StatusEffectsComponent>(uid, out var status))
        {
            return;
        }
        switch (args.NewMobState)
        {
            case MobState.Alive:
            case MobState.SoftCritical:
                {
                    break;
                }
            case MobState.Critical:
            case MobState.HardCritical:
            case MobState.Dead:
                {
                    _statusEffect.TryRemoveStatusEffect(uid, "Stun");
                    break;
                }
            case MobState.Invalid:
            default:
                return;
        }

    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<SharedStunSystem>();
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<KnockedDownComponent>();
        while (query.MoveNext(out var uid, out var knocked))
        {
            if (!knocked.AutoStand || knocked.DoAfterId != null || knocked.NextUpdate > _timing.CurTime)
                continue;

            if (!TryStanding(uid, knocked) && knocked.NextUpdate <= _timing.CurTime)
                ScheduleAutoStandRetry(uid, knocked);
        }
    }

    private void OnStunShutdown(Entity<StunnedComponent> ent, ref ComponentShutdown args)
    {
        // This exists so the client can end their funny animation if they're playing one.
        UpdateCanMove(ent, ent.Comp, args);
        Appearance.RemoveData(ent, StunVisuals.SeeingStars);
    }

    private void UpdateCanMove(EntityUid uid, StunnedComponent component, EntityEventArgs args)
    {
        _blocker.UpdateCanMove(uid);
    }

    private void OnStunOnContactStartup(Entity<StunOnContactComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<PhysicsComponent>(ent, out var body))
            _broadphase.RegenerateContacts((ent, body));
    }

    private void OnStunOnContactCollide(Entity<StunOnContactComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != ent.Comp.FixtureId)
            return;

        if (_entityWhitelist.IsBlacklistPass(ent.Comp.Blacklist, args.OtherEntity))
            return;

        if (!TryComp<StatusEffectsComponent>(args.OtherEntity, out var status))
            return;

        TryStun(args.OtherEntity, ent.Comp.Duration, true, status);
        TryKnockdown(args.OtherEntity, ent.Comp.Duration, true, status);
    }

    private void OnKnockInit(EntityUid uid, KnockedDownComponent component, ComponentInit args)
    {
        var dirty = false;

        if (component.NextUpdate == TimeSpan.Zero)
        {
            component.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.5f);
            dirty = true;
        }

        RefreshKnockedMovement(uid, component);
        _standingState.Down(uid, true, false);

        if (dirty)
            Dirty(uid, component);
    }

    private void OnKnockShutdown(EntityUid uid, KnockedDownComponent component, ComponentShutdown args)
    {
        _nextToggleKnockdownAt.Remove(uid);
        _nextStandAttemptAt.Remove(uid);
        component.FrictionModifier = 1f;
        component.SpeedModifier = 1f;
        component.DoAfterId = null;
        _standingState.Stand(uid);
    }

    private void ScheduleAutoStandRetry(EntityUid uid, KnockedDownComponent component)
    {
        var nextUpdate = _timing.CurTime + AutoStandRetryDelay;
        if (component.NextUpdate >= nextUpdate)
            return;

        component.NextUpdate = nextUpdate;
        Dirty(uid, component);
    }

    private void RefreshKnockedMovement(EntityUid uid, KnockedDownComponent component)
    {
        var ev = new KnockedDownRefreshEvent();
        RaiseLocalEvent(uid, ref ev);

        if (MathHelper.CloseTo(component.SpeedModifier, ev.SpeedModifier) &&
            MathHelper.CloseTo(component.FrictionModifier, ev.FrictionModifier))
            return;

        component.SpeedModifier = ev.SpeedModifier;
        component.FrictionModifier = ev.FrictionModifier;
        Dirty(uid, component);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);
    }

    private void OnKnockedRefreshSpeed(EntityUid uid, KnockedDownComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.SpeedModifier);
    }

    private void OnCrawlerKnockedRefresh(EntityUid uid, CrawlerComponent component, ref KnockedDownRefreshEvent args)
    {
        args.SpeedModifier *= component.SpeedModifier;
        args.FrictionModifier *= component.FrictionModifier;
    }

    private void OnCrawlerDamaged(EntityUid uid, CrawlerComponent component, ref global::Content.Shared.Damage.DamageChangedEvent args)
    {
        if (!TryComp(uid, out KnockedDownComponent? knocked) || !args.DamageIncreased || args.DamageDelta == null)
            return;

        if (args.DamageDelta.GetTotal() >= component.KnockdownDamageThreshold)
        {
            var nextUpdate = _timing.CurTime + component.DefaultKnockedDuration;
            if (knocked.NextUpdate < nextUpdate)
            {
                knocked.NextUpdate = nextUpdate;
                Dirty(uid, knocked);
            }
        }
    }

    private void OnHandEquippedWhileKnocked(EntityUid uid, KnockedDownComponent component, ref DidEquipHandEvent args)
    {
        if (_timing.ApplyingState)
            return;

        RefreshKnockedMovement(uid, component);
    }

    private void OnHandUnequippedWhileKnocked(EntityUid uid, KnockedDownComponent component, ref DidUnequipHandEvent args)
    {
        if (_timing.ApplyingState)
            return;

        RefreshKnockedMovement(uid, component);
    }

    private void OnHandCountChangedWhileKnocked(EntityUid uid, KnockedDownComponent component, ref HandCountChangedEvent args)
    {
        if (_timing.ApplyingState)
            return;

        RefreshKnockedMovement(uid, component);
    }

    private void HandleToggleKnockdown(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { } uid || !_cfg.GetCVar(CCVars.MovementCrawling))
            return;

        if (!Exists(uid))
        {
            _nextToggleKnockdownAt.Remove(uid);
            _nextStandAttemptAt.Remove(uid);
            return;
        }

        if (!HasComp<CrawlerComponent>(uid))
            return;

        if (TryComp(uid, out KnockedDownComponent? activeKnocked) && activeKnocked.DoAfterId.HasValue)
            return;

        if (_nextToggleKnockdownAt.TryGetValue(uid, out var nextToggle) && _timing.CurTime < nextToggle)
            return;

        _nextToggleKnockdownAt[uid] = _timing.CurTime + ToggleKnockdownCooldown;

        if (!TryComp(uid, out KnockedDownComponent? knocked))
        {
            EnsureComp<KnockedDownComponent>(uid);
            knocked = Comp<KnockedDownComponent>(uid);
            knocked.AutoStand = false;
            if (TryComp(uid, out CrawlerComponent? crawler))
                knocked.NextUpdate = _timing.CurTime + crawler.DefaultKnockedDuration;
            Dirty(uid, knocked);
            return;
        }

        var stand = true;
        if (_nextStandAttemptAt.TryGetValue(uid, out var nextStandAttempt) && _timing.CurTime < nextStandAttempt)
            return;

        if (knocked.AutoStand != stand)
        {
            knocked.AutoStand = stand;
            Dirty(uid, knocked);
        }

        if (!TryStanding(uid, knocked, popupOnBlocked: true))
        {
            if (knocked.DoAfterId.HasValue)
            {
                _doAfter.Cancel(new DoAfterId(uid, knocked.DoAfterId.Value));
                knocked.DoAfterId = null;
                Dirty(uid, knocked);
            }

            _nextStandAttemptAt[uid] = _timing.CurTime + ManualStandAttemptCooldown;
        }
        else
        {
            _nextStandAttemptAt[uid] = _timing.CurTime + ManualStandAttemptCooldown;
        }
    }

    private bool IntersectingStandingColliders(EntityUid uid)
    {
        if (!TryComp(uid, out TransformComponent? xformComp))
            return false;

        var standingLayers = (int) (CollisionGroup.MidImpassable | CollisionGroup.HighImpassable);
        var fixtureQuery = GetEntityQuery<FixturesComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();
        var ourAabb = _entityLookup.GetAABBNoContainer(uid, xformComp.MapPosition.Position, xformComp.WorldRotation);

        var intersecting = _entityLookup.GetEntitiesIntersecting(xformComp.MapID, ourAabb, LookupFlags.Static | LookupFlags.Dynamic);

        foreach (var ent in intersecting)
        {
            if (ent == uid)
                continue;

            if (!fixtureQuery.TryGetComponent(ent, out var fixtures) || !xformQuery.TryComp(ent, out var xformOther))
                continue;

            var xform = new Transform(xformOther.MapPosition.Position, xformOther.WorldRotation);
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard || (fixture.CollisionMask & standingLayers) == 0)
                    continue;

                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                {
                    var intersection = fixture.Shape.ComputeAABB(xform, i).IntersectPercentage(ourAabb);
                    if (intersection > 0.1f)
                        return true;
                }
            }
        }

        return false;
    }

    private bool TryStanding(EntityUid uid, KnockedDownComponent? knocked = null, bool popupOnBlocked = false)
    {
        if (!Resolve(uid, ref knocked, false))
            return true;

        if (knocked.NextUpdate > _timing.CurTime || !_blocker.CanMove(uid))
            return false;

        if (IntersectingStandingColliders(uid))
        {
            if (popupOnBlocked)
                _popup.PopupClient(Loc.GetString("knockdown-component-stand-no-room"), uid, uid, PopupType.SmallCaution);

            ScheduleAutoStandRetry(uid, knocked);
            return false;
        }

        if (!TryComp(uid, out CrawlerComponent? crawler) || !_cfg.GetCVar(CCVars.MovementCrawling))
        {
            RemComp<KnockedDownComponent>(uid);
            return true;
        }

        if (knocked.DoAfterId != null)
            return false;

        var doAfter = new DoAfterArgs(EntityManager, uid, crawler.StandTime, new TryStandDoAfterEvent(), uid, uid)
        {
            BreakOnDamage = true,
            DamageThreshold = 5f,
            CancelDuplicate = true,
            RequireCanInteract = false,
            BreakOnHandChange = true
        };

        if (!_doAfter.TryStartDoAfter(doAfter, out var id))
            return false;

        knocked.DoAfterId = id.Value.Index;
        Dirty(uid, knocked);
        return true;
    }

    private void OnStandDoAfter(EntityUid uid, KnockedDownComponent knocked, ref TryStandDoAfterEvent args)
    {
        knocked.DoAfterId = null;

        if (args.Cancelled || !_blocker.CanMove(uid))
        {
            Dirty(uid, knocked);
            return;
        }

        if (IntersectingStandingColliders(uid))
        {
            _popup.PopupClient(Loc.GetString("knockdown-component-stand-no-room"), uid, uid, PopupType.SmallCaution);
            ScheduleAutoStandRetry(uid, knocked);
            return;
        }

        RemComp<KnockedDownComponent>(uid);
    }

    private void OnStandAttempt(EntityUid uid, KnockedDownComponent component, StandAttemptEvent args)
    {
        if (component.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    private void OnSlowInit(EntityUid uid, SlowedDownComponent component, ComponentInit args)
    {
        _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);
    }

    private void OnSlowRemove(EntityUid uid, SlowedDownComponent component, ComponentShutdown args)
    {
        component.SprintSpeedModifier = 1f;
        component.WalkSpeedModifier = 1f;
        _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefreshMovespeed(EntityUid uid, SlowedDownComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.WalkSpeedModifier, component.SprintSpeedModifier);
    }

    // TODO STUN: Make events for different things. (Getting modifiers, attempt events, informative events...)

    /// <summary>
    ///     Stuns the entity, disallowing it from doing many interactions temporarily.
    /// </summary>
    public bool TryStun(EntityUid uid, TimeSpan time, bool refresh,
        StatusEffectsComponent? status = null)
    {
        time *= _modify.GetModifier(uid); // Goobstation

        if (time <= TimeSpan.Zero)
            return false;

        if (!Resolve(uid, ref status, false))
            return false;

        if (!_statusEffect.TryAddStatusEffect<StunnedComponent>(uid, "Stun", time, refresh))
            return false;

        var ev = new StunnedEvent();
        RaiseLocalEvent(uid, ref ev);

        _adminLogger.Add(LogType.Stamina, LogImpact.Medium, $"{ToPrettyString(uid):user} stunned for {time.Seconds} seconds");
        return true;
    }

    public bool TryCrawling(EntityUid uid, bool refresh = true, bool autoStand = true)
    {
        return TryCrawling(uid, null, refresh, autoStand);
    }

    public void TrySetKnockedDownFrictionModifier(EntityUid uid, float frictionModifier, KnockedDownComponent? knocked = null)
    {
        if (!Resolve(uid, ref knocked, false))
            return;

        var newModifier = Math.Min(knocked.FrictionModifier, frictionModifier);
        if (MathHelper.CloseTo(knocked.FrictionModifier, newModifier))
            return;

        knocked.FrictionModifier = newModifier;
        Dirty(uid, knocked);
    }

    public bool TryCrawling(EntityUid uid, TimeSpan? time, bool refresh = true, bool autoStand = true)
    {
        if (!TryComp(uid, out CrawlerComponent? crawler))
            return false;

        if (time == null)
            time = crawler.DefaultKnockedDuration;

        if (!TryKnockdown(uid, time.Value, refresh))
            return false;

        if (TryComp(uid, out KnockedDownComponent? knocked) && knocked.AutoStand != autoStand)
        {
            knocked.AutoStand = autoStand;
            Dirty(uid, knocked);
        }

        return true;
    }

    /// <summary>
    ///     Knocks down the entity, making it fall to the ground.
    /// </summary>
    public bool TryKnockdown(EntityUid uid, TimeSpan time, bool refresh,
        StatusEffectsComponent? status = null)
    {
        time *= _modify.GetModifier(uid); // Goobstation

        if (time <= TimeSpan.Zero)
            return false;

        if (!Resolve(uid, ref status, false))
            return false;

        if (!_statusEffect.TryAddStatusEffect<KnockedDownComponent>(uid, "KnockedDown", time, refresh))
            return false;

        if (TryComp(uid, out KnockedDownComponent? knocked))
        {
            var dirty = false;
            var nextUpdate = _timing.CurTime + time;

            if (knocked.NextUpdate < nextUpdate)
            {
                knocked.NextUpdate = nextUpdate;
                dirty = true;
            }

            if (!knocked.AutoStand)
            {
                knocked.AutoStand = true;
                dirty = true;
            }

            if (dirty)
                Dirty(uid, knocked);
        }

        var ev = new KnockedDownEvent();
        RaiseLocalEvent(uid, ref ev);

        return true;
    }

    /// <summary>
    ///     Applies knockdown and stun to the entity temporarily.
    /// </summary>
    public bool TryParalyze(EntityUid uid, TimeSpan time, bool refresh,
        StatusEffectsComponent? status = null)
    {
        if (!Resolve(uid, ref status, false))
            return false;

        return TryKnockdown(uid, time, refresh, status) && TryStun(uid, time, refresh, status);
    }

    /// <summary>
    ///     Slows down the mob's walking/running speed temporarily
    /// </summary>
    public bool TrySlowdown(EntityUid uid, TimeSpan time, bool refresh,
        float walkSpeedMultiplier = 1f, float runSpeedMultiplier = 1f,
        StatusEffectsComponent? status = null)
    {
        if (!Resolve(uid, ref status, false))
            return false;

        if (time <= TimeSpan.Zero)
            return false;

        if (_statusEffect.TryAddStatusEffect<SlowedDownComponent>(uid, "SlowedDown", time, refresh, status))
        {
            var slowed = Comp<SlowedDownComponent>(uid);
            // Doesn't make much sense to have the "TrySlowdown" method speed up entities now does it?
            walkSpeedMultiplier = Math.Clamp(walkSpeedMultiplier, 0f, 1f);
            runSpeedMultiplier = Math.Clamp(runSpeedMultiplier, 0f, 1f);

            slowed.WalkSpeedModifier *= walkSpeedMultiplier;
            slowed.SprintSpeedModifier *= runSpeedMultiplier;

            _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);

            return true;
        }

        return false;
    }

    private void OnKnockedTileFriction(EntityUid uid, KnockedDownComponent component, ref TileFrictionEvent args)
    {
        args.Modifier *= component.FrictionModifier;
    }

    #region Attempt Event Handling

    private void OnMoveAttempt(EntityUid uid, StunnedComponent stunned, UpdateCanMoveEvent args)
    {
        if (stunned.LifeStage > ComponentLifeStage.Running)
            return;

        args.Cancel();
    }

    private void OnAttempt(EntityUid uid, StunnedComponent stunned, CancellableEntityEventArgs args)
    {
        args.Cancel();
    }

    private void OnEquipAttempt(EntityUid uid, StunnedComponent stunned, IsEquippingAttemptEvent args)
    {
        // is this a self-equip, or are they being stripped?
        if (args.Equipee == uid)
            args.Cancel();
    }

    private void OnUnequipAttempt(EntityUid uid, StunnedComponent stunned, IsUnequippingAttemptEvent args)
    {
        // is this a self-equip, or are they being stripped?
        if (args.Unequipee == uid)
            args.Cancel();
    }

    #endregion
}

/// <summary>
///     Raised directed on an entity when it is stunned.
/// </summary>
[ByRefEvent]
public record struct StunnedEvent;

/// <summary>
///     Raised directed on an entity when it is knocked down.
/// </summary>
[ByRefEvent]
public record struct KnockedDownEvent;

[ByRefEvent]
public record struct KnockedDownRefreshEvent()
{
    public float SpeedModifier = 1f;
    public float FrictionModifier = 1f;
}


[ByRefEvent]
[Serializable, NetSerializable]
public sealed partial class TryStandDoAfterEvent : SimpleDoAfterEvent;
