using System;
using TTTXO.Game.Services;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI
{
    /// <summary>
    /// Paints an avatar-and-frame pair with whatever the player currently has equipped, and keeps it in
    /// step with <see cref="CosmeticSelection.Changed"/>.
    ///
    /// Three screens show the same portrait - Profile, Home's header and the in-match turn bar - and each
    /// one would otherwise repeat the same "strip the old class, resolve the new one, subscribe to
    /// changes" dance. Getting that wrong in one of them means a stale avatar somewhere, which is exactly
    /// the kind of thing nobody notices until a player changes their look and one screen disagrees.
    /// </summary>
    public sealed class EquippedAvatarView
    {
        private readonly VisualElement _avatar;
        private readonly VisualElement _frame;

        /// <summary>
        /// Binds to an avatar element and its frame overlay. Both may be null - a screen that shows only
        /// the portrait without a frame is legitimate - and the pair repaints immediately so the caller
        /// never has to prime it.
        /// </summary>
        public EquippedAvatarView(VisualElement avatar, VisualElement frame)
        {
            _avatar = avatar;
            _frame = frame;

            CosmeticSelection.Changed += Apply;
            Apply();
        }

        public void Apply()
        {
            Repaint(_avatar, CosmeticCatalog.Avatars, CosmeticSelection.AvatarId);
            Repaint(_frame, CosmeticCatalog.Frames, CosmeticSelection.FrameId);
        }

        private static void Repaint(VisualElement element, System.Collections.Generic.IReadOnlyList<CosmeticCatalog.Option> options, string id)
        {
            if (element == null)
            {
                return;
            }

            foreach (string styleClass in CosmeticCatalog.AllStyleClasses(options))
            {
                element.RemoveFromClassList(styleClass);
            }

            element.AddToClassList(CosmeticCatalog.StyleClassFor(options, id));
        }
    }
}
