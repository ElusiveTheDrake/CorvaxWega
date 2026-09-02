using Content.Shared.Teleportation.Components;
using Robust.Client.GameObjects;
using Content.Shared.Humanoid;
using Content.Client.Body;
using Content.Shared.Body;

namespace Content.Client.Teleportation;

public sealed partial class ShadekinCoreSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private VisualBodySystem _visualBody = default!;
    [Dependency] private BodySystem _bodySystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShadekinCoreComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<ShadekinCoreComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ShadekinCoreComponent, OrganGotRemovedEvent>(OnCoreRemoved);
        SubscribeLocalEvent<ShadekinCoreComponent, OrganGotInsertedEvent>(OnCoreInserted);
    }

    private void OnStartup(Entity<ShadekinCoreComponent> ent, ref ComponentStartup args) => UpdateSprite(ent);

    private void OnHandleState(Entity<ShadekinCoreComponent> ent, ref AfterAutoHandleStateEvent args) => UpdateSprite(ent);

    private void UpdateSprite(Entity<ShadekinCoreComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var state = ent.Comp.Mode == ShadekinCoreMode.Active
            ? ent.Comp.ActiveState
            : ent.Comp.InactiveState;

        _sprite.LayerSetRsiState((ent.Owner, sprite), 0, state);

        if (!TryComp<OrganComponent>(ent, out var organ) || organ.Body is not { } body)
            return;

        var bright = ent.Comp.Mode == ShadekinCoreMode.Active && ent.Comp.OwnerBody == body;
        UpdateEyesFor(ent, body, bright);
    }
    private void OnCoreRemoved(Entity<ShadekinCoreComponent> ent, ref OrganGotRemovedEvent args)
    {
        UpdateEyesFor(ent, args.Target, bright: false); // no core in body -> always burned
    }

    private void OnCoreInserted(Entity<ShadekinCoreComponent> ent, ref OrganGotInsertedEvent args)
    {
        var bright = ent.Comp.Mode == ShadekinCoreMode.Active && ent.Comp.OwnerBody == args.Target;
        UpdateEyesFor(ent, args.Target, bright);
    }

    private void UpdateEyesFor(Entity<ShadekinCoreComponent> ent, EntityUid body, bool bright)
    {
        var color = bright ? ent.Comp.BrightColor : ent.Comp.BurnedColor;
        var targetV = Color.ToHsv(color).Z;

        foreach (var (eyeEnt, _, visualOrgan) in _bodySystem.EnumerateOrgans<VisualOrganComponent>(body))
        {
            if (!_visualBody.IsOrganLayer((eyeEnt, visualOrgan), HumanoidVisualLayers.Eyes))
                continue;

            var currentHsv = Color.ToHsv(_visualBody.GetOrganColor((eyeEnt, visualOrgan)));
            currentHsv.Z = targetV;
            _visualBody.SetVisualOrganColor((eyeEnt, visualOrgan), Color.FromHsv(currentHsv));
        }
    }
}
