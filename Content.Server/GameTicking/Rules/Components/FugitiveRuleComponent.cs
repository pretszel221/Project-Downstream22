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

    [DataField]
    public string FugitivePrefRole = "Fugitive";

    [DataField]
    public string HunterPrefRole = "FugitiveHunter";

    [DataField]
    public string FugitiveObjectivePrototype = "FugitiveSurviveObjective";

    [DataField]
    public List<string> HunterObjectivePrototypes = new() { "FugitiveHunterCaptureQuotaObjective" };

    [DataField]
    public string HunterBriefingPrefLoc = "fugitive-hunter-role-briefing-preference";

    [DataField]
    public string HunterBriefingTargetsLoc = "fugitive-hunter-role-briefing";

    [DataField]
    public string HunterBriefingNoTargetsLoc = "fugitive-hunter-role-briefing-no-targets";

    [DataField]
    public string ShipPinpointerTargetLoc = "fugitive-hunter-ship-pinpointer-target";

    [DataField]
    public string BountyNoTargetLoc = "fugitive-hunter-bounty-pinpointer-no-target";

    [DataField]
    public bool FugitivesAreInitialInfected;

    [DataField]
    public float InitialInfectedGraceSeconds = 180f;
}
