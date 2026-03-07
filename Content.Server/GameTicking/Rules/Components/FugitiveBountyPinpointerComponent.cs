// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

namespace Content.Server.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class FugitiveBountyPinpointerComponent : Component
{
    [DataField]
    public float CooldownSeconds = 180f;

    [DataField]
    public float ActiveSeconds = 30f;

    [ViewVariables]
    public float TimeRemaining;

    [ViewVariables]
    public bool ActiveTracking;
}
