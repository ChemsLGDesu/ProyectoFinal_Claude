using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI
{
    /// <summary>
    /// Keeps every screen's content inside <see cref="Screen.safeArea"/>.
    ///
    /// UI Toolkit has no safe area support of its own, so without this the notch sits on top of whatever
    /// a screen puts in its first band of pixels. Measured on an iPhone 13 Pro Max: 102px of camera
    /// cutout against a Home header that starts 32px down, which is what cut the player name in half.
    ///
    /// The inset is applied as <em>padding on each screen</em> rather than on the document root, and that
    /// choice is load-bearing: padding lives inside the element's own box, so a screen's background image
    /// still bleeds edge to edge under the notch while its content moves out from under it. Padding on
    /// the root would have inset the backgrounds too and left black bars top and bottom.
    /// </summary>
    public sealed class SafeAreaController
    {
        /// <summary>Base padding a screen wants around its content, before the safe area is added.
        /// Declared in main.uss next to the `padding` rule it mirrors - see the comment on
        /// <see cref="TryGetBasePadding"/> for why this is a custom property and not a read of
        /// <c>resolvedStyle.padding</c>.</summary>
        private static readonly CustomStyleProperty<float> BasePaddingX = new("--content-padding-x");
        private static readonly CustomStyleProperty<float> BasePaddingY = new("--content-padding-y");

        /// <summary>How often the safe area is re-checked, in milliseconds. The geometry callback covers
        /// every resize; this only exists to recover from a frame where the values were incoherent and no
        /// further resize was coming.</summary>
        private const long RecheckIntervalMs = 500;

        private readonly VisualElement _root;
        private readonly IReadOnlyList<VisualElement> _screens;

        private Rect _appliedSafeArea = new(-1f, -1f, -1f, -1f);
        private float _appliedPanelWidth = -1f;

        public SafeAreaController(VisualElement root, IReadOnlyList<VisualElement> screens)
        {
            _root = root;
            _screens = screens;

            // Rotation, resolution changes and the Device Simulator switching phones all reach us as a
            // panel resize.
            _root.RegisterCallback<GeometryChangedEvent>(_ => Apply());

            // The safety net. An earlier version relied on the geometry callback alone and shipped the
            // wrong padding for a whole session: it ran on a frame where Screen and the panel disagreed,
            // wrote garbage, and the "nothing changed" early-out then kept it from ever recomputing.
            // Re-checking on a timer makes a bad frame self-correcting instead of permanent.
            _root.schedule.Execute(Apply).Every(RecheckIntervalMs);

            Apply();
        }

        /// <summary>Re-applies the inset. Safe to call at any time; does nothing when the safe area and
        /// the panel width are both unchanged, and nothing when the frame's values are not yet
        /// self-consistent.</summary>
        public void Apply()
        {
            float panelWidth = _root.parent != null ? _root.parent.layout.width : _root.layout.width;
            if (!IsFrameCoherent(panelWidth))
            {
                return;
            }

            var safeArea = Screen.safeArea;
            if (safeArea == _appliedSafeArea && Mathf.Approximately(panelWidth, _appliedPanelWidth))
            {
                return;
            }

            // Screen.safeArea is in pixels and the panel is in its own units, so everything crosses over
            // by the same factor the panel scale uses. safeArea.y is measured from the bottom.
            float unitsPerPixel = panelWidth / Screen.width;
            float insetLeft = Mathf.Max(0f, safeArea.x) * unitsPerPixel;
            float insetRight = Mathf.Max(0f, Screen.width - safeArea.xMax) * unitsPerPixel;
            float insetBottom = Mathf.Max(0f, safeArea.y) * unitsPerPixel;
            float insetTop = Mathf.Max(0f, Screen.height - safeArea.yMax) * unitsPerPixel;

            bool appliedToEvery = true;

            foreach (var screen in _screens)
            {
                if (screen == null || !TryGetBasePadding(screen, out float baseX, out float baseY))
                {
                    // Styles for this screen have not resolved yet. Skip it and make sure we come back:
                    // recording this pass as applied would strand the screen without its inset.
                    appliedToEvery = false;
                    continue;
                }

                screen.style.paddingLeft = baseX + insetLeft;
                screen.style.paddingRight = baseX + insetRight;
                screen.style.paddingTop = baseY + insetTop;
                screen.style.paddingBottom = baseY + insetBottom;
            }

            if (appliedToEvery)
            {
                _appliedSafeArea = safeArea;
                _appliedPanelWidth = panelWidth;
            }
        }

        /// <summary>
        /// Whether this frame's screen metrics can be trusted.
        ///
        /// In the Editor a frame can report the Device Simulator's <c>safeArea</c> next to the Game view's
        /// <c>Screen.width</c> - each value plausible, the pair meaningless. Applying that mix produced an
        /// inset computed at the wrong scale and a negative top inset. The width of the safe area has to
        /// match the width of the screen it came from; when it does not, this frame gets skipped.
        /// </summary>
        private static bool IsFrameCoherent(float panelWidth)
        {
            if (float.IsNaN(panelWidth) || panelWidth <= 0f)
            {
                return false;
            }

            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return false;
            }

            var safeArea = Screen.safeArea;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                return false;
            }

            // Width must match the screen the safe area came from, and the area must fit inside that
            // screen vertically - a yMax past the bottom is the stale-Screen.height case, and it is what
            // produced a negative top inset.
            return Mathf.Approximately(safeArea.width, Screen.width)
                && safeArea.yMax <= Screen.height + 1f;
        }

        /// <summary>
        /// The screen's own content padding, read from a custom USS property.
        ///
        /// Deliberately not <c>resolvedStyle.paddingTop</c>: once this controller writes an inline
        /// padding, <c>resolvedStyle</c> reports that inline value, so re-reading it would add the inset
        /// on top of the previous inset on every pass. Caching the first read instead only moves the
        /// problem - the first read can land before styles resolve, and a padding of 0 looks exactly like
        /// a real one, so the wrong base gets cached permanently. A custom property is never written by
        /// this class, so it always reports what the stylesheet says.
        /// </summary>
        private static bool TryGetBasePadding(VisualElement screen, out float baseX, out float baseY)
        {
            var custom = screen.customStyle;
            bool hasX = custom.TryGetValue(BasePaddingX, out baseX);
            bool hasY = custom.TryGetValue(BasePaddingY, out baseY);
            return hasX && hasY;
        }
    }
}
