using Content.Server.Administration.Logs;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Server.Audio;
using Content.Shared.Actions;
using Content.Server.Popups;
using Content.Shared.Popups;
using Content.Shared.Database;
using Robust.Shared.Timing;
using Content.Shared.Actions.Components;
using Robust.Shared.Spawners;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Gibbing;
using Content.Shared.Zombies;
using Content.Shared.Vampire.Components;
using Content.Shared.Changeling.Components;
using Content.Shared.Body;

namespace Content.Server.Teleportation;

public sealed partial class ActionTeleporterSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private LinkedEntitySystem _link = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedDoAfterSystem _doafter = default!;
    [Dependency] private BodySystem _body = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ActionTeleporterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ActionTeleporterComponent, ActionCloseTeleportShadekin>(OnClosePortalAction);
        SubscribeLocalEvent<ActionTeleporterComponent, ActionTeleportShadekin>(OnCreatePortalAction);
        SubscribeLocalEvent<ActionTeleporterComponent, EntityTeleportedFromPortalEvent>(OnShadekinPortalTeleport);
        SubscribeLocalEvent<ActionTeleporterComponent, ActionTeleporterDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<ActionTeleporterComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ActionTeleporterComponent, BeingGibbedEvent>(OnGibbed);
        SubscribeLocalEvent<ActionTeleporterComponent, EntityZombifiedEvent>(OnZombified);
        SubscribeLocalEvent<VampireComponent, ComponentInit>(OnBecameVampire);
        SubscribeLocalEvent<ChangelingIdentityComponent, ComponentStartup>(OnBecameChangeling);
        SubscribeLocalEvent<ActionTeleporterComponent, OrganRemovedFromEvent>(OnCoreRemoved);
        SubscribeLocalEvent<ActionTeleporterComponent, OrganInsertedIntoEvent>(OnCoreInserted);
    }

    private void OnMapInit(EntityUid uid, ActionTeleporterComponent component, MapInitEvent args)
    {
        _actions.AddAction(uid, ref component.CreatePortalActionEntity, component.CreatePortalAction);
    }

    private void OnCreatePortalAction(EntityUid uid, ActionTeleporterComponent component, ActionTeleportShadekin args)
    {
        if (args.Handled)
            return;

        var doafterArgs = new DoAfterArgs(EntityManager, args.Performer, component.PortalCreationDelay,
            new ActionTeleporterDoAfterEvent(), uid, used: uid)
        {
            BreakOnDamage = component.DoAfterBreakOnDamage,
            BreakOnMove = component.DoAfterBreakOnMove,
            MovementThreshold = component.DoAfterMovementThreshold,
        };

        _doafter.TryStartDoAfter(doafterArgs);
        args.Handled = true;
    }

    private void OnDoAfter(EntityUid uid, ActionTeleporterComponent component, ActionTeleporterDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        HandlePortalUpdating(uid, component, args.Args.User);
        args.Handled = true;
    }

    private void HandlePortalUpdating(EntityUid uid, ActionTeleporterComponent component, EntityUid user)
    {
        if (Deleted(user))
            return;

        var xform = Transform(user);

        // Create the first portal.
        if (Deleted(component.FirstPortal) && Deleted(component.SecondPortal))
        {
            if (xform.ParentUid != xform.GridUid)
                return;
            var timeout = EnsureComp<PortalTimeoutComponent>(user);
            timeout.EnteredPortal = null;
            component.FirstPortal = Spawn(component.FirstPortalPrototype, Transform(user).Coordinates);
            component.FirstPortalDeadline = _timing.CurTime + component.FirstPortalTimeout;
            Dirty(uid, component);
            _appearance.SetData(component.FirstPortal.Value, PortalVisuals.State, PortalVisualState.Closed);

            if (TryComp<PortalComponent>(component.FirstPortal, out var portal))
            {
                portal.RandomTeleport = false;
                if (component.AllowPortalsOnDifferentMaps)
                    portal.CanTeleportToOtherMaps = true;
            }

            _adminLogger.Add(LogType.EntitySpawn, LogImpact.High, $"{ToPrettyString(user):player} opened {ToPrettyString(component.FirstPortal.Value)} at {Transform(component.FirstPortal.Value).Coordinates} using {ToPrettyString(uid)}");
            _audio.PlayPvs(component.NewPortalSound, uid);
            _actions.RemoveAction(uid, component.CreatePortalActionEntity);
            _actions.AddAction(uid, ref component.CreateSecondPortalActionEntity, component.CreateSecondPortalAction);
        }
        else if (Deleted(component.SecondPortal))
        {
            if (xform.ParentUid != xform.GridUid)
                return;

            if (!component.AllowPortalsOnDifferentGrids && xform.ParentUid != Transform(component.FirstPortal!.Value).ParentUid)
            {
                FizzlePortals((uid, component), user, true);
                return;
            }
            var timeout = EnsureComp<PortalTimeoutComponent>(user);
            timeout.EnteredPortal = null;
            component.SecondPortal = Spawn(component.SecondPortalPrototype, Transform(user).Coordinates);
            component.FirstPortalDeadline = null;
            component.PortalsCloseDeadline = _timing.CurTime + component.PortalsCloseTimeout;
            Dirty(uid, component);
            _link.TryLink(component.FirstPortal!.Value, component.SecondPortal.Value, true);

            _appearance.SetData(component.FirstPortal.Value, PortalVisuals.State, PortalVisualState.Opening);
            _appearance.SetData(component.SecondPortal.Value, PortalVisuals.State, PortalVisualState.Opening);

            if (TryComp<PortalComponent>(component.FirstPortal, out var portal))
            {
                portal.RandomTeleport = false;
                if (component.AllowPortalsOnDifferentMaps)
                    portal.CanTeleportToOtherMaps = true;
            }

            _adminLogger.Add(LogType.EntitySpawn, LogImpact.High, $"{ToPrettyString(user):player} opened {ToPrettyString(component.SecondPortal.Value)} at {Transform(component.SecondPortal.Value).Coordinates} linked to {ToPrettyString(component.FirstPortal!.Value)} using {ToPrettyString(uid)}");
            _link.TryLink(component.FirstPortal!.Value, component.SecondPortal.Value, true);
            _audio.PlayPvs(component.NewPortalSound, uid);
            _actions.RemoveAction(uid, component.CreateSecondPortalActionEntity);
            _actions.AddAction(uid, ref component.ClosePortalsActionEntity, component.ClosePortalsAction);
        }
        else
        {
            FizzlePortals((uid, component), user, false);
        }
    }

    public void FizzlePortals(Entity<ActionTeleporterComponent> entity, EntityUid? user, bool instability)
    {
        var portalStrings = "";
        portalStrings += ToPrettyString(entity.Comp.FirstPortal);
        if (portalStrings != "")
            portalStrings += " and ";
        portalStrings += ToPrettyString(entity.Comp.SecondPortal);
        if (portalStrings != "")
        {
            if (user != null)
                _adminLogger.Add(LogType.EntityDelete, LogImpact.High, $"{ToPrettyString(user):player} closed {portalStrings} with {ToPrettyString(entity)}");
            else
                _adminLogger.Add(LogType.EntityDelete, LogImpact.High, $"{portalStrings} were closed");
        }

        if (!Deleted(entity.Comp.FirstPortal))
        {
            _appearance.SetData(entity.Comp.FirstPortal.Value, PortalVisuals.State, PortalVisualState.Closing);

            var lifetime = TryComp<PortalComponent>(entity.Comp.FirstPortal.Value, out var firstPortalComp)
                ? firstPortalComp.ClosingAnimationTime
                : TimeSpan.FromSeconds(0.5);

            var despawn1 = EnsureComp<TimedDespawnComponent>(entity.Comp.FirstPortal.Value);
            despawn1.Lifetime = (float)lifetime.TotalSeconds;
        }

        if (!Deleted(entity.Comp.SecondPortal))
        {
            _appearance.SetData(entity.Comp.SecondPortal.Value, PortalVisuals.State, PortalVisualState.Closing);

            var lifetime2 = TryComp<PortalComponent>(entity.Comp.SecondPortal.Value, out var secondPortalComp)
                ? secondPortalComp.ClosingAnimationTime
                : TimeSpan.FromSeconds(0.5);

            var despawn2 = EnsureComp<TimedDespawnComponent>(entity.Comp.SecondPortal.Value);
            despawn2.Lifetime = (float)lifetime2.TotalSeconds;
        }
        var hadSecondPortal = entity.Comp.SecondPortal != null;

        entity.Comp.FirstPortal = null;
        entity.Comp.SecondPortal = null;
        entity.Comp.FirstPortalDeadline = null;
        entity.Comp.PortalsCloseDeadline = null;
        Dirty(entity);
        _audio.PlayPvs(entity.Comp.ClearPortalsSound, entity);
        if (TryComp(entity.Comp.CreateSecondPortalActionEntity, out ActionComponent? secondActionComp)
            && secondActionComp.AttachedEntity == entity.Owner)
        {
            _actions.RemoveAction(entity.Owner, entity.Comp.CreateSecondPortalActionEntity);
        }

        if (TryComp(entity.Comp.ClosePortalsActionEntity, out ActionComponent? closeActionComp)
            && closeActionComp.AttachedEntity == entity.Owner)
        {
            _actions.RemoveAction(entity.Owner, entity.Comp.ClosePortalsActionEntity);
        }

        _actions.SetCooldown(entity.Comp.CreatePortalActionEntity,
                hadSecondPortal ? entity.Comp.TwoPortalCooldown : entity.Comp.OnePortalCooldown);

        _actions.AddAction(entity.Owner, ref entity.Comp.CreatePortalActionEntity, entity.Comp.CreatePortalAction);

        if (instability && user != null)
            _popup.PopupEntity(Loc.GetString("action-teleporter-instability-fizzle"), entity, user.Value, PopupType.MediumCaution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ActionTeleporterComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.FirstPortalDeadline != null && curTime >= comp.FirstPortalDeadline)
            {
                FizzlePortals((uid, comp), null, false);
                continue;
            }

            if (comp.PortalsCloseDeadline != null && curTime >= comp.PortalsCloseDeadline)
            {
                FizzlePortals((uid, comp), null, false);
            }
        }
    }
    private void OnClosePortalAction(EntityUid uid, ActionTeleporterComponent component, ActionCloseTeleportShadekin args)
    {
        if (args.Handled)
            return;

        FizzlePortals((uid, component), args.Performer, false);
        args.Handled = true;
    }

    private void OnShadekinPortalTeleport(EntityUid uid, ActionTeleporterComponent component, EntityTeleportedFromPortalEvent args) // No chain-portals for ya
    {
        if (!component.FizzleOnForeignPortal)
            return;

        if (component.FirstPortal == null && component.SecondPortal == null)
            return; // no portals of their own, nothing to fizzle

        if (args.EnteredPortal == component.FirstPortal || args.EnteredPortal == component.SecondPortal)
            return; // their own portal, do nothing

        _popup.PopupEntity(Loc.GetString("action-teleporter-popup-fizzle-foreign-portal"), uid);
        FizzlePortals((uid, component), uid, false);
    }
    private void OnMobStateChanged(EntityUid uid, ActionTeleporterComponent component, MobStateChangedEvent args) // Can't support portals if you dead
    {
        if (args.NewMobState != MobState.Dead)
            return;

        FizzlePortals((uid, component), null, false);
    }

    private void OnGibbed(EntityUid uid, ActionTeleporterComponent component, ref BeingGibbedEvent args) // Can't support portals if you de.... oh god
    {
        FizzlePortals((uid, component), null, false);
    }

    private void OnZombified(EntityUid uid, ActionTeleporterComponent component, ref EntityZombifiedEvent args) // Gruaa can't use it - core dead
    {
        FizzlePortals((uid, component), null, false);

        _actions.RemoveAction(uid, component.CreatePortalActionEntity);
        _actions.RemoveAction(uid, component.CreateSecondPortalActionEntity);
    }

    private void OnBecameVampire(Entity<VampireComponent> ent, ref ComponentInit args) // RedSpace and Core(BlueSpace) not compatible
    {
        if (!TryComp<ActionTeleporterComponent>(ent.Owner, out var component))
            return;

        RemoveTeleport(ent.Owner, component);
    }

    private void OnBecameChangeling(Entity<ChangelingIdentityComponent> ent, ref ComponentStartup args) // Core to difficult to mimic
    {
        if (!TryComp<ActionTeleporterComponent>(ent, out var component))
            return;

        RemoveTeleport(ent, component); // your shared cleanup helper
    }

    private void RemoveTeleport(EntityUid uid, ActionTeleporterComponent component)
    {
        FizzlePortals((uid, component), null, false);

        _actions.RemoveAction(uid, component.CreatePortalActionEntity);
        _actions.RemoveAction(uid, component.CreateSecondPortalActionEntity);
    }
    private bool HasWorkingCore(EntityUid uid)
    {
        foreach (var organ in _body.EnumerateOrgans<ShadekinCoreComponent>(uid))
        {
            if (organ.Comp2.Mode == ShadekinCoreMode.Active && organ.Comp2.OwnerBody == uid)
                return true;
        }
        return false;
    }

    private void OnCoreRemoved(EntityUid uid, ActionTeleporterComponent component, ref OrganRemovedFromEvent args)
    {
        if (!HasWorkingCore(uid))
        {
            FizzlePortals((uid, component), null, false);
            _actions.RemoveAction(uid, component.CreatePortalActionEntity);
            _actions.RemoveAction(uid, component.CreateSecondPortalActionEntity);

            component.CreatePortalActionEntity = null;
            component.CreateSecondPortalActionEntity = null;
        }
    }

    private void OnCoreInserted(EntityUid uid, ActionTeleporterComponent component, ref OrganInsertedIntoEvent args)
    {
        if (HasWorkingCore(uid) && component.CreatePortalActionEntity == null)
            _actions.AddAction(uid, ref component.CreatePortalActionEntity, component.CreatePortalAction);
    }
}
