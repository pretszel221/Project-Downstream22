// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

using Content.Server.GameTicking.Rules.Components;
using Content.Server.Ghost;
using Content.Server.Popups;
using Content.Server.Roles;
using Content.Server.Storage.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Server.GameObjects;
using Content.Shared.Verbs;

namespace Content.Server.GameTicking.Rules;

public sealed class FugitiveCaptureLockerSystem : EntitySystem
{
    [Dependency] private readonly GhostSystem _ghost = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _role = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FugitiveCaptureLockerComponent, GetVerbsEvent<InteractionVerb>>(OnCaptureVerb);
    }

    private void OnCaptureVerb(Entity<FugitiveCaptureLockerComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (!TryComp<EntityStorageComponent>(ent, out var storage) || storage.Open)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("fugitive-capture-locker-confirm"),
            Priority = 5,
            Act = () => ConfirmCapture(ent.Owner, args.User, storage)
        });
    }

    private void ConfirmCapture(EntityUid locker, EntityUid user, EntityStorageComponent storage)
    {
        foreach (var occupant in storage.Contents.ContainedEntities.ToArray())
        {
            if (!TryComp<CuffableComponent>(occupant, out var cuffable) || cuffable.CuffedHandCount <= 0)
                continue;

            if (!_mind.TryGetMind(occupant, out var mindId, out var mind) || !_role.MindHasRole<FugitiveRoleComponent>((mindId, mind), out _))
                continue;

            // Send target to ghost and round-remove their body.
            _ghost.SpawnGhost((mindId, mind), Transform(locker).Coordinates, canReturn: false);
            QueueDel(occupant);

            var ev = new FugitiveCapturedEvent(mindId);
            RaiseLocalEvent(ref ev);
            _popup.PopupEntity(Loc.GetString("fugitive-capture-locker-success"), locker, user);
            return;
        }

        _popup.PopupEntity(Loc.GetString("fugitive-capture-locker-incorrect-target"), locker, user);
    }
}

public readonly record struct FugitiveCapturedEvent(EntityUid FugitiveMindId);
