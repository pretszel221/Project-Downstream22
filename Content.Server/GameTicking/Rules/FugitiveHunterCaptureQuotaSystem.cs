// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

using System;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Objectives.Components;
using Robust.Server.GameObjects;

namespace Content.Server.GameTicking.Rules;

public sealed class FugitiveHunterCaptureQuotaSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FugitiveHunterCaptureQuotaConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<FugitiveHunterCaptureQuotaConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
    }

    private void OnAfterAssign(Entity<FugitiveHunterCaptureQuotaConditionComponent> ent, ref ObjectiveAfterAssignEvent args)
    {
        var (captured, total) = GetCaptureCounts();
        _meta.SetEntityName(ent.Owner, Loc.GetString("fugitive-hunter-capture-quota-title", ("captured", captured), ("total", total)), args.Meta);
    }

    private void OnGetProgress(Entity<FugitiveHunterCaptureQuotaConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        var (captured, total) = GetCaptureCounts();
        var clampedTotal = Math.Max(1, total);
        args.Progress = Math.Clamp((float) captured / clampedTotal, 0f, 1f);

        _meta.SetEntityName(ent.Owner, Loc.GetString("fugitive-hunter-capture-quota-title", ("captured", captured), ("total", total)));
    }

    private (int captured, int total) GetCaptureCounts()
    {
        var query = EntityQueryEnumerator<FugitiveRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out _, out var fugitiveRule, out _))
        {
            return (fugitiveRule.CapturedFugitiveMinds.Count, fugitiveRule.TotalFugitives);
        }

        return (0, 0);
    }
}
