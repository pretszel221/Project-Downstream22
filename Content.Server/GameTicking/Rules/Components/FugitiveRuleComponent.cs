// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class FugitiveRuleComponent : Component
{
    [DataField]
    public HashSet<EntityUid> HunterShuttleGrids = new();

    [DataField]
    public int TotalFugitives;

    [DataField]
    public int CapturedFugitives;

    [DataField]
    public HashSet<EntityUid> FugitiveMinds = new();

    [DataField]
    public HashSet<EntityUid> CapturedFugitiveMinds = new();
}
