// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

using Content.Shared.GridPreloader.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class FugitiveRuleComponent : Component
{
    [DataField]
    public List<ProtoId<PreloadedGridPrototype>> HunterShuttles = new();

    [DataField]
    public HashSet<EntityUid> HunterShuttleGrids = new();

    [DataField]
    public int TotalFugitives;

    [DataField]
    public HashSet<EntityUid> FugitiveMinds = new();

    [DataField]
    public HashSet<EntityUid> CapturedFugitiveMinds = new();
}
