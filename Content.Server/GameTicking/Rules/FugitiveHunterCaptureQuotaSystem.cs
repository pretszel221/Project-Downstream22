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
        SubscribeLocalEvent<FugitiveCapturedEvent>(OnFugitiveCaptured);
    }

    private void OnAfterAssign(Entity<FugitiveHunterCaptureQuotaConditionComponent> ent, ref ObjectiveAfterAssignEvent args)
    {
        var counts = GetCaptureCounts();
        var title = GetObjectiveTitle(ent.Comp.TitleLoc, counts.captured, counts.total);
        _meta.SetEntityName(ent.Owner, title, args.Meta);
    }

    private void OnGetProgress(Entity<FugitiveHunterCaptureQuotaConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        var (captured, total) = GetCaptureCounts();
        var clampedTotal = Math.Max(1, total);
        args.Progress = Math.Clamp((float) captured / clampedTotal, 0f, 1f);

        _meta.SetEntityName(ent.Owner, GetObjectiveTitle(ent.Comp.TitleLoc, captured, total));
    }

    private void OnFugitiveCaptured(FugitiveCapturedEvent args)
    {
        var (captured, total) = GetCaptureCounts();
        var query = EntityQueryEnumerator<FugitiveHunterCaptureQuotaConditionComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var quota, out var meta))
        {
            _meta.SetEntityName(uid, GetObjectiveTitle(quota.TitleLoc, captured, total), meta);
        }
    }

    private string GetObjectiveTitle(string locKey, int captured, int total)
    {
        return Loc.GetString(locKey, ("captured", captured), ("total", total));
    }

    private (int captured, int total) GetCaptureCounts()
    {
        var query = EntityQueryEnumerator<FugitiveRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out _, out var fugitiveRule, out _))
        {
            return (fugitiveRule.CapturedFugitives, fugitiveRule.TotalFugitives);
        }

        return (0, 0);
    }
}
