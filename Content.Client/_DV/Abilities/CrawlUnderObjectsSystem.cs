using Content.Shared._DV.Abilities;
// using Content.Shared.Popups; // WOLFGATE(Species): unused, dropped when this system was reworked to use SpriteSystem
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._DV.Abilities;

// WOLFGATE(Species) START: documents the sneak depth swap
/// <summary>
/// Client half of sneaking: drops the sprite to the depth mice use while the appearance data says the mob is
/// sneaking, and puts the old depth back afterwards.
/// </summary>
// WOLFGATE END
public sealed partial class HideUnderTableAbilitySystem : SharedCrawlUnderObjectsSystem
{
    [Dependency] private SpriteSystem _sprite = default!; // WOLFGATE(Species): was AppearanceSystem

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrawlUnderObjectsComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    // WOLFGATE(Species) START: reworked to the Entity<T>/ref-event pattern and SpriteSystem.SetDrawDepth
    // upstream original, kept for merges:
    // private void OnAppearanceChange(EntityUid uid,
    //     CrawlUnderObjectsComponent component,
    //     AppearanceChangeEvent args)
    // {
    //     if (!TryComp<SpriteComponent>(uid, out var sprite))
    //         return;
    //
    //     _appearance.TryGetData(uid, SneakMode.Enabled, out bool enabled);
    //     if (enabled)
    //     {
    //         if (component.OriginalDrawDepth != null)
    //             return;
    //
    //         component.OriginalDrawDepth = sprite.DrawDepth;
    //         sprite.DrawDepth = (int) DrawDepth.SmallMobs;
    //     }
    //     else
    //     {
    //         if (component.OriginalDrawDepth == null)
    //             return;
    //
    //         sprite.DrawDepth = (int) component.OriginalDrawDepth;
    //         component.OriginalDrawDepth = null;
    //     }
    // }

    private void OnAppearanceChange(Entity<CrawlUnderObjectsComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite || !args.AppearanceData.TryGetValue(SneakMode.Enabled, out var value))
            return;

        if (value is true)
        {
            if (ent.Comp.OriginalDrawDepth != null)
                return;

            ent.Comp.OriginalDrawDepth = sprite.DrawDepth;
            _sprite.SetDrawDepth((ent.Owner, sprite), (int) DrawDepth.SmallMobs);
        }
        else
        {
            if (ent.Comp.OriginalDrawDepth is not { } original)
                return;

            _sprite.SetDrawDepth((ent.Owner, sprite), original);
            ent.Comp.OriginalDrawDepth = null;
        }
    }
    // WOLFGATE END
}
