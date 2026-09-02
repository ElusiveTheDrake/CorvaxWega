using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Teleportation.Components;

public sealed partial class ActionTeleportShadekin : InstantActionEvent
{
}
public sealed partial class ActionCloseTeleportShadekin : InstantActionEvent
{
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActionTeleporterComponent : Component

{
    [DataField, AutoNetworkedField]
    public EntityUid? FirstPortal;

    [DataField, AutoNetworkedField]
    public EntityUid? SecondPortal;

    [DataField, AutoNetworkedField]
    public TimeSpan? FirstPortalDeadline;

    [DataField, AutoNetworkedField]
    public TimeSpan? PortalsCloseDeadline;

    [DataField, AutoNetworkedField]
    public EntityUid? CreatePortalActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? CreateSecondPortalActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? ClosePortalsActionEntity;

    [DataField]
    public TimeSpan FirstPortalTimeout = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan PortalsCloseTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    ///     Should the portals be able to be placed across grids?
    /// </summary>
    [DataField]
    public bool AllowPortalsOnDifferentGrids;

    /// <summary>
    ///     Should the portals work across maps?
    /// </summary>
    [DataField]
    public bool AllowPortalsOnDifferentMaps;

    [DataField]
    public EntProtoId FirstPortalPrototype = "PortalRed";

    [DataField]
    public EntProtoId SecondPortalPrototype = "PortalBlue";

    [DataField]
    public SoundSpecifier NewPortalSound =
        new SoundPathSpecifier("/Audio/Machines/high_tech_confirm.ogg")
        {
            Params = AudioParams.Default.AddVolume(-2f)
        };

    [DataField]
    public SoundSpecifier ClearPortalsSound = new SoundPathSpecifier("/Audio/Machines/button.ogg");

    [DataField]
    public EntProtoId CreatePortalAction = "ActionCreatePortalShadekin";

    [DataField]
    public EntProtoId CreateSecondPortalAction = "ActionCreateSecondPortalShadekin";

    [DataField]
    public EntProtoId ClosePortalsAction = "ActionClosePortalsShadekin";

    [DataField]
    public bool FizzleOnForeignPortal = true;

    [DataField]
    public TimeSpan OnePortalCooldown = TimeSpan.FromSeconds(60);

    [DataField]
    public TimeSpan TwoPortalCooldown = TimeSpan.FromSeconds(300);

    [DataField]
    public float PortalCreationDelay = 1.0f;

    [DataField]
    public bool DoAfterBreakOnDamage = true;

    [DataField]
    public bool DoAfterBreakOnMove = true;

    [DataField]
    public float DoAfterMovementThreshold = 0.5f;
}

[Serializable, NetSerializable]
public sealed partial class ActionTeleporterDoAfterEvent : SimpleDoAfterEvent;
