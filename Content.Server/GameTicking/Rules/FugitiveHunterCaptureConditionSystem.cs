// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

using Content.Server.GameTicking.Rules.Components;
using Content.Server.Objectives.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Objectives.Components;

namespace Content.Server.GameTicking.Rules;

/// <summary>
/// Completes fugitive hunter target objectives when that target has been captured in a fugitive locker.
/// </summary>
public sealed class FugitiveHunterCaptureConditionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FugitiveHunterCaptureConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    private void OnGetProgress(Entity<FugitiveHunterCaptureConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        args.Progress = 0f;

        if (!TryComp<TargetObjectiveComponent>(ent, out var target) || target.Target == null)
            return;

        var query = EntityQueryEnumerator<FugitiveRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out _, out var rule, out _))
        {
            if (rule.CapturedFugitiveMinds.Contains(target.Target.Value))
                args.Progress = 1f;

            break;
        }
    }
}
