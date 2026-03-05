using Robust.Shared.GameStates;

namespace Content.Shared.Stunnable;

/// <summary>
/// Marks an entity as being able to crawl via knockdown.
/// Ported from upstream Wizden crawling behavior.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedStunSystem))]
public sealed partial class CrawlerComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan DefaultKnockedDuration { get; set; } = TimeSpan.FromSeconds(0.5);

    [DataField, AutoNetworkedField]
    public float KnockdownDamageThreshold = 5f;

    [DataField, AutoNetworkedField]
    public TimeSpan StandTime = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField]
    public float SpeedModifier = 0.4f;

    [DataField, AutoNetworkedField]
    public float FrictionModifier = 1f;
}

