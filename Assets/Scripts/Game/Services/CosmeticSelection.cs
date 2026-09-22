using System;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>
    /// Which profile cosmetics the player has equipped: the avatar portrait and the frame ringing it.
    ///
    /// Stored in PlayerPrefs, the same device-local convenience pattern as <see cref="RankedProfileCache"/>
    /// and the soft-currency mirror, and echoed into the Cloud Save <c>profile</c> item by
    /// <see cref="PlayerDataService"/> so the choice follows a linked account to another device.
    ///
    /// This is a PREFERENCE, not an entitlement. It records which cosmetic is displayed, never which ones
    /// the player is allowed to use. Every avatar and frame is free today, so the two happen to coincide;
    /// once the store sells them, ownership has to be decided server-side and validated there
    /// (Docs/01-Directrices-Proyecto.md: purchases are settled by Cloud Code, never by the client). At that
    /// point the equip UI filters its options by an owned-list the server sends - it does NOT get to grant
    /// itself a cosmetic by writing an id here.
    /// </summary>
    public static class CosmeticSelection
    {
        private const string AvatarKey = "TTTXO.Cosmetic.Avatar";
        private const string FrameKey = "TTTXO.Cosmetic.Frame";
        private const string BannerKey = "TTTXO.Cosmetic.Banner";
        private const string PieceSkinKey = "TTTXO.Cosmetic.PieceSkin";
        private const string BoardSkinKey = "TTTXO.Cosmetic.BoardSkin";

        /// <summary>Equipped when the player has never chosen, and the fallback whenever a stored id is no longer in the catalog.</summary>
        public const string DefaultAvatarId = "fox";

        public const string DefaultFrameId = "neon";

        public const string DefaultBannerId = "neon";

        public const string DefaultPieceSkinId = "neon";

        public const string DefaultBoardSkinId = "neon";

        /// <summary>Raised after either selection changes, so an open screen can restyle without polling.</summary>
        public static event Action Changed;

        public static string AvatarId => PlayerPrefs.GetString(AvatarKey, DefaultAvatarId);

        public static string FrameId => PlayerPrefs.GetString(FrameKey, DefaultFrameId);

        public static void SetAvatar(string avatarId)
        {
            SetIfChanged(AvatarKey, avatarId, DefaultAvatarId);
        }

        public static void SetFrame(string frameId)
        {
            SetIfChanged(FrameKey, frameId, DefaultFrameId);
        }

        public static string BannerId => PlayerPrefs.GetString(BannerKey, DefaultBannerId);

        public static void SetBanner(string bannerId)
        {
            SetIfChanged(BannerKey, bannerId, DefaultBannerId);
        }

        public static string PieceSkinId => PlayerPrefs.GetString(PieceSkinKey, DefaultPieceSkinId);

        public static void SetPieceSkin(string pieceSkinId)
        {
            SetIfChanged(PieceSkinKey, pieceSkinId, DefaultPieceSkinId);
        }

        public static string BoardSkinId => PlayerPrefs.GetString(BoardSkinKey, DefaultBoardSkinId);

        public static void SetBoardSkin(string boardSkinId)
        {
            SetIfChanged(BoardSkinKey, boardSkinId, DefaultBoardSkinId);
        }

        private static void SetIfChanged(string key, string value, string fallback)
        {
            string resolved = string.IsNullOrEmpty(value) ? fallback : value;
            if (PlayerPrefs.GetString(key, fallback) == resolved)
            {
                return;
            }

            PlayerPrefs.SetString(key, resolved);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Applies a cloud snapshot. Ignores ids this build does not know so a save written by a newer version cannot leave the player with a blank avatar.</summary>
        public static void ApplyCloudSnapshot(string avatarId, string frameId, string bannerId, string pieceSkinId, string boardSkinId)
        {
            if (!string.IsNullOrEmpty(avatarId))
            {
                PlayerPrefs.SetString(AvatarKey, avatarId);
            }

            if (!string.IsNullOrEmpty(frameId))
            {
                PlayerPrefs.SetString(FrameKey, frameId);
            }

            if (!string.IsNullOrEmpty(bannerId))
            {
                PlayerPrefs.SetString(BannerKey, bannerId);
            }

            if (!string.IsNullOrEmpty(pieceSkinId))
            {
                PlayerPrefs.SetString(PieceSkinKey, pieceSkinId);
            }

            if (!string.IsNullOrEmpty(boardSkinId))
            {
                PlayerPrefs.SetString(BoardSkinKey, boardSkinId);
            }

            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
