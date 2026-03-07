// SPDX-FileCopyrightText: 2026 OpenAI
//
// SPDX-License-Identifier: MIT

namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// Marks an entity as a valid fugitive capture target for the fugitive locker,
/// even if they no longer have an attached mind (e.g. dead/ghosted body).
/// </summary>
[RegisterComponent]
public sealed partial class FugitiveCaptureTargetComponent : Component
{
    [DataField]
    public bool Captured;
}
