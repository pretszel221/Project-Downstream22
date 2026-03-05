// SPDX-FileCopyrightText: 2024 Tadeo <td12233a@gmail.com>
// SPDX-FileCopyrightText: 2024 whateverusername0 <whateveremail>
// SPDX-FileCopyrightText: 2025 taydeo <td12233a@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later AND MIT

using Content.Shared._White.Standing;
using Content.Shared.Buckle;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._White.Standing;

public sealed class LayingDownSystem : SharedLayingDownSystem
{
    private static readonly Angle LayingLeft = Angle.FromDegrees(270);
    private static readonly Angle LayingRight = Angle.FromDegrees(90);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LayingDownComponent, MoveEvent>(OnMovementInput);

        SubscribeNetworkEvent<CheckAutoGetUpEvent>(OnCheckAutoGetUp);
    }

    private void OnMovementInput(EntityUid uid, LayingDownComponent component, MoveEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!_standing.IsDown(uid))
            return;

        if (_buckle.IsBuckled(uid) || HasComp<KnockedDownComponent>(uid))
            return;

        if (_animation.HasRunningAnimation(uid, "rotate"))
            return;

        if (!TryComp<TransformComponent>(uid, out var transform)
            || !TryComp<SpriteComponent>(uid, out var sprite)
            || !TryComp<RotationVisualsComponent>(uid, out var rotationVisuals))
        {
            return;
        }

        var rotation = transform.LocalRotation + (_eyeManager.CurrentEye.Rotation - (transform.LocalRotation - transform.WorldRotation));

        var target = rotation.GetDir() is Direction.SouthEast or Direction.East or Direction.NorthEast or Direction.North
            ? LayingLeft
            : LayingRight;

        if (rotationVisuals.HorizontalRotation == target && sprite.Rotation == target)
            return;

        rotationVisuals.HorizontalRotation = target;
        sprite.Rotation = target;
    }

    private void OnCheckAutoGetUp(CheckAutoGetUpEvent ev, EntitySessionEventArgs args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var uid = GetEntity(ev.User);

        if (HasComp<KnockedDownComponent>(uid))
            return;

        if (!TryComp<TransformComponent>(uid, out var transform) || !TryComp<RotationVisualsComponent>(uid, out var rotationVisuals))
            return;

        var rotation = transform.LocalRotation + (_eyeManager.CurrentEye.Rotation - (transform.LocalRotation - transform.WorldRotation));

        var target = rotation.GetDir() is Direction.SouthEast or Direction.East or Direction.NorthEast or Direction.North
            ? LayingLeft
            : LayingRight;

        if (rotationVisuals.HorizontalRotation == target)
            return;

        rotationVisuals.HorizontalRotation = target;
    }
}
