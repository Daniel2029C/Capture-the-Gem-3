using CTG.Classes;
using CTG.Combat;
using CTG.Core;
using CTG.Gameplay;
using CTG.Items;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// Draws a <see cref="PlayerCharacter"/>: places it at its interpolated position, picks the body (the class's art with
    /// its team outline, or a placeholder), the swung item, the drawn bow and nocked arrow, the offering orb and the
    /// grapple hooks. It only reads the player's public state, so the simulation never depends on how it looks; a headless
    /// run simply never adds one.
    /// </summary>
    public sealed class PlayerView : MonoBehaviour
    {
        // The placeholder sprite is 3 tiles (48 px) tall.
        const int k_SpriteWidthPx = 24;
        const int k_SpriteHeightPx = 48;
        const float k_DefaultBowGripTiles = 9f * 1.41421356f / SimUnits.PixelsPerTile;

        PlayerCharacter m_Player;
        Team m_BodyTeam = Team.Neutral;
        SpriteRenderer m_Body;
        /// <summary>The class's character art when it has some; otherwise the placeholder body is drawn.</summary>
        CharacterSprite m_Art;
        /// <summary>How far out along the aim the drawn bow sits, in tiles: where the class art's hand grips it, or the default.</summary>
        float m_BowGripTiles = k_DefaultBowGripTiles;
        Transform m_HeldItemPivot;
        SpriteRenderer m_HeldItem;
        Transform m_BowPivot;
        SpriteRenderer m_BowSprite;
        SpriteRenderer m_NockedArrow;
        SpriteRenderer m_OfferingMarker;

        /// <summary>Adds a view to the player and starts drawing it.</summary>
        public static PlayerView Create(PlayerCharacter player)
        {
            var view = player.gameObject.AddComponent<PlayerView>();
            view.Bind(player);
            return view;
        }

        void Bind(PlayerCharacter player)
        {
            m_Player = player;
            BuildVisuals();
            player.ClassChanged += OnClassChanged;
            if (player.Class != null) OnClassChanged(player.Class);
        }

        void OnDestroy()
        {
            if (m_Player != null) m_Player.ClassChanged -= OnClassChanged;
        }

        void BuildVisuals()
        {
            m_Body = gameObject.AddComponent<SpriteRenderer>();
            m_BodyTeam = m_Player.Team;
            m_Body.sprite = PlaceholderSprites.Player(k_SpriteWidthPx, k_SpriteHeightPx, TeamColors.For(m_Player.Team));
            m_Body.sortingOrder = 10;
            m_Art = CharacterSprite.Create(transform, 10);

            var pivot = new GameObject("Held Item");
            m_HeldItemPivot = pivot.transform;
            m_HeldItemPivot.SetParent(transform, false);
            m_HeldItemPivot.localPosition = SwingProfile.Overhead.HandOffsetPx / SimUnits.PixelsPerTile;

            var item = new GameObject("Sprite");
            item.transform.SetParent(m_HeldItemPivot, false);
            m_HeldItem = item.AddComponent<SpriteRenderer>();
            m_HeldItem.sortingOrder = 11;
            m_HeldItem.enabled = false;

            // Drawn bow: pivots on the body centre and turns to the aim.
            m_BowPivot = new GameObject("Bow").transform;
            m_BowPivot.SetParent(transform, false);
            m_BowPivot.localPosition = new Vector3(0f, PlayerCharacter.HitboxHeightPx * 0.5f / SimUnits.PixelsPerTile, 0f);

            m_BowSprite = new GameObject("Bow Sprite").AddComponent<SpriteRenderer>();
            m_BowSprite.transform.SetParent(m_BowPivot, false);
            m_BowSprite.sortingOrder = 12;
            m_BowSprite.enabled = false;

            m_NockedArrow = new GameObject("Nocked Arrow").AddComponent<SpriteRenderer>();
            m_NockedArrow.transform.SetParent(m_BowPivot, false);
            m_NockedArrow.sortingOrder = 13;
            m_NockedArrow.enabled = false;

            // A glowing orb above the head while carrying an offering, so enemies can see who to hunt.
            m_OfferingMarker = new GameObject("Offering Marker").AddComponent<SpriteRenderer>();
            m_OfferingMarker.transform.SetParent(transform, false);
            m_OfferingMarker.transform.localPosition = new Vector3(0f, (k_SpriteHeightPx + 10f) / SimUnits.PixelsPerTile, 0f);
            m_OfferingMarker.sprite = PlaceholderSprites.Solid("offering_orb", new Color32(255, 215, 90, 255), 8, 8);
            m_OfferingMarker.sortingOrder = 14;
            m_OfferingMarker.enabled = false;

            var hookView = new GameObject("Grapple Hooks").AddComponent<GrappleHookView>();
            hookView.transform.SetParent(transform, false);
            hookView.Bind(m_Player);
        }

        /// <summary>The player took a class: use its art, and put the drawn bow where the art's hands hold it.</summary>
        void OnClassChanged(ClassDef def)
        {
            ClassSpriteSet art = ClassSpriteSet.For(def.Id);
            m_Art.SetSpriteSet(art);
            // With class art, the bow sits where the art's hands hold it; otherwise at the hitbox centre.
            bool hasArt = art != null && art.HasFrames;
            m_BowPivot.localPosition = hasArt
                ? (Vector3)(art.BowPivotPx / ClassSpriteSet.PixelsPerUnit)
                : new Vector3(0f, PlayerCharacter.HitboxHeightPx * 0.5f / SimUnits.PixelsPerTile, 0f);
            m_BowGripTiles = hasArt ? art.GripDistancePx / ClassSpriteSet.PixelsPerUnit : k_DefaultBowGripTiles;
        }

        void LateUpdate()
        {
            PlayerCharacter player = m_Player;
            if (player == null || player.Motor == null) return;

            PlayerMotor motor = player.Motor;
            ItemUser itemUser = player.ItemUser;
            float alpha = MatchContext.RenderAlpha;
            Vector2 px = motor.GetVisualPosition(alpha);
            transform.position = new Vector3((px.x + motor.Width * 0.5f) / SimUnits.PixelsPerTile,
                                             px.y / SimUnits.PixelsPerTile, 0f);

            bool alive = !player.Health.IsDead;
            m_Body.enabled = alive;
            m_Body.flipX = motor.Direction < 0;
            if (m_BodyTeam != player.Team)
            {
                m_BodyTeam = player.Team;
                m_Body.sprite = PlaceholderSprites.Player(k_SpriteWidthPx, k_SpriteHeightPx, TeamColors.For(player.Team));
            }

            // Burning flickers orange; blink while immune after a hit.
            Color tint = Color.white;
            if (player.Effects.Has(StatusEffectDefs.Burning)) tint = Color.Lerp(Color.white, new Color(1f, 0.5f, 0.15f), 0.55f + 0.25f * Mathf.Sin(Time.time * 18f));
            if (player.Health.IsImmune && (Time.frameCount / 4) % 2 == 0) tint.a = 0.4f;
            m_Body.color = tint;

            // Class art, when it exists, replaces the placeholder body (with its team outline as a separate layer).
            bool hasArt = m_Art.HasArt;
            if (hasArt) m_Body.enabled = false;
            ChargeBow bow = itemUser.Bow;
            bool swinging = itemUser.ItemAnimation > 0 && !bow.Active && ItemDefs.Get(itemUser.UsingItem).UseStyle == UseStyle.Swing;
            float swingAngle = 0f;
            if (swinging)
                swingAngle = MeleeSwing.Angle(itemUser.ItemAnimation - alpha, itemUser.ItemAnimationMax, ItemDefs.Get(itemUser.UsingItem).Swing);
            m_Art.Show(new CharacterPose
            {
                SwingAngle = swingAngle,
                Visible = hasArt, Alive = alive, Facing = motor.Direction, Grounded = motor.Grounded, Velocity = motor.Velocity,
                DoubleJumping = motor.DoubleJumping, FastFalling = motor.IsFastFalling, Sprinting = player.Abilities.RunSpeedScale > 1.01f,
                Hit = player.Health.IsImmune, Drawing = bow.IsCharging, DrawFraction = bow.ChargeFraction, Released = bow.Active && bow.Released,
                AimAngle = bow.AimAngle, Swinging = swinging,
                SwingProgress = itemUser.ItemAnimationMax > 0 ? 1f - itemUser.ItemAnimation / (float)itemUser.ItemAnimationMax : 0f,
                TeamColor = TeamColors.For(player.Team), Tint = tint,
            });

            UpdateHeldItem(player, alpha, alive);
            UpdateBow(player, alive);
            // Enemies can't see the orb: only the carrier and their team can.
            m_OfferingMarker.enabled = alive && player.Offerings > 0 && player.OfferingVisibleToLocal;
            if (m_OfferingMarker.enabled)
            {
                float pulse = 1f + 0.2f * Mathf.Sin(Time.time * 6f);
                m_OfferingMarker.transform.localScale = Vector3.one * pulse * (1f + 0.25f * (Mathf.Min(player.Offerings, 4) - 1));
            }
        }

        void UpdateBow(PlayerCharacter player, bool alive)
        {
            ChargeBow bow = player.ItemUser.Bow;
            bool drawn = alive && bow.Active && bow.Item != null;
            m_BowSprite.enabled = drawn;
            m_NockedArrow.enabled = drawn && !bow.Released;
            if (!drawn) return;

            float angle = bow.AimAngle * Mathf.Rad2Deg;
            m_BowPivot.localRotation = Quaternion.Euler(0f, 0f, angle);
            // Facing left, mirror across the aim line so the bow isn't upside down.
            m_BowPivot.localScale = new Vector3(1f, bow.Direction, 1f);

            // Bow out along the aim (9·√2 px, or where the class art's hand grips it); arrow nocked 7·√2 px beyond the bow,
            // pulled back up to 13 px by charge.
            const float px = 1f / SimUnits.PixelsPerTile;
            m_BowSprite.sprite = PlaceholderSprites.Bow(bow.Item.IconColor);
            m_BowSprite.transform.localPosition = new Vector3(m_BowGripTiles, 0f, 0f);

            ChargeBowStats stats = bow.Item.ChargeBow;
            float pullback = Mathf.Lerp(0f, 13f, Mathf.Min(bow.Charge, stats.MaxCharge) / stats.MaxCharge);
            m_NockedArrow.sprite = PlaceholderSprites.Arrow(ProjectileDefs.Get(bow.Arrow).Color);
            m_NockedArrow.transform.localPosition = new Vector3(m_BowGripTiles + (7f * 1.41421356f - pullback) * px, 0f, 0f);

            // At full charge the arrow alternates between slightly brighter and slightly dimmer every 200 ticks.
            Color tint = Color.white;
            if (bow.FullyCharged)
            {
                tint = bow.FullChargeTicks % 400 <= 200
                    ? Color.Lerp(tint, Color.white, 0.2f)
                    : Color.Lerp(tint, new Color32(150, 150, 150, 255), 0.2f);
            }

            m_NockedArrow.color = tint;
        }

        void UpdateHeldItem(PlayerCharacter player, float alpha, bool alive)
        {
            ItemUser itemUser = player.ItemUser;
            ItemDef def = ItemDefs.Get(itemUser.UsingItem);
            bool swinging = alive && itemUser.IsUsing && def.UseStyle == UseStyle.Swing;
            m_HeldItem.enabled = swinging;
            if (!swinging) return;

            if (def.IsPickaxe)
            {
                m_HeldItem.sprite = PlaceholderSprites.Pickaxe(def.IconColor);
                // The placeholder sprite is 16 px; scale it to the item's real reach so the visual matches hits.
                m_HeldItem.transform.localScale = Vector3.one * (def.SwingLengthPx / SimUnits.PixelsPerTile);
            }
            else
            {
                m_HeldItem.sprite = PlaceholderSprites.Square($"icon_{def.Id}", def.IconColor, 8);
                m_HeldItem.transform.localScale = Vector3.one;
            }

            float angle = MeleeSwing.Angle(itemUser.ItemAnimation - alpha, itemUser.ItemAnimationMax, def.Swing);
            int direction = itemUser.UseDirection;
            m_HeldItemPivot.localPosition = def.Swing.HandOffsetPx / SimUnits.PixelsPerTile;
            // With a jointed arm the item's pivot rides in the fist, so the grip stays in the hand through the swing.
            if (m_Art.SwingRigActive)
            {
                Vector3 hand = m_Art.SwingHandWorld;
                m_HeldItemPivot.position = new Vector3(hand.x, hand.y, m_HeldItemPivot.position.z);
            }

            m_HeldItemPivot.localScale = new Vector3(direction, 1f, 1f);
            m_HeldItemPivot.localRotation = Quaternion.Euler(0f, 0f, angle * direction);
        }
    }
}
