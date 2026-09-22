using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>Lifecycle of the Unity Gaming Services session, exposed for UI to poll/react to.</summary>
    public enum UgsInitStatus
    {
        NotStarted,
        Initializing,
        Ready,
        Failed
    }

    /// <summary>
    /// Initializes Unity Gaming Services core + signs in anonymously, with retry/backoff
    /// (Docs/03-Arquitectura-UGS-TicTacToe.md#Authentication: "Sign-in anonimo desde el dia 1").
    /// The Splash screen awaits <see cref="Status"/> becoming <see cref="UgsInitStatus.Ready"/>
    /// before navigating to Home; on <see cref="UgsInitStatus.Failed"/> it shows a generic,
    /// non-technical error with a Retry action (Docs/06-Wireframes-UI.md screen 1: "En fallo de
    /// init: pantalla de error generica con boton Retry").
    ///
    /// This never blocks Milestone 1 gameplay: local/1P modes work identically with or without a
    /// session (see GameConfigService's local fallback and the design doc's Milestone 1 scope).
    /// </summary>
    public static class UgsInitializer
    {
        private const int MaxAttempts = 4;
        private const int BaseRetryDelayMilliseconds = 1000;

        public static UgsInitStatus Status { get; private set; } = UgsInitStatus.NotStarted;

        /// <summary>Last exception message, kept for logs only - never shown to the player (see UiText.Splash.ErrorMessage).</summary>
        public static string LastErrorMessage { get; private set; }

        private static Task<UgsInitStatus> _inFlightInit;

        /// <summary>
        /// Runs UnityServices.InitializeAsync + anonymous sign-in with exponential backoff between
        /// attempts. Safe to call repeatedly - returns the same in-flight task while Initializing,
        /// and a completed task immediately once Ready.
        /// </summary>
        public static Task<UgsInitStatus> InitializeAsync()
        {
            if (Status == UgsInitStatus.Ready)
            {
                return Task.FromResult(Status);
            }

            if (_inFlightInit != null && Status == UgsInitStatus.Initializing)
            {
                return _inFlightInit;
            }

            _inFlightInit = RunInitializeAsync();
            return _inFlightInit;
        }

        /// <summary>Lets the Splash "Retry" button re-run <see cref="InitializeAsync"/> from a clean state.</summary>
        public static void ResetForRetry()
        {
            if (Status == UgsInitStatus.Failed)
            {
                Status = UgsInitStatus.NotStarted;
                _inFlightInit = null;
            }
        }

        private static async Task<UgsInitStatus> RunInitializeAsync()
        {
            Status = UgsInitStatus.Initializing;
            LastErrorMessage = null;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    if (UnityServices.State != ServicesInitializationState.Initialized)
                    {
                        await UnityServices.InitializeAsync();
                    }

                    if (!AuthenticationService.Instance.IsSignedIn)
                    {
                        await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    }

                    Status = UgsInitStatus.Ready;
                    return Status;
                }
                catch (Exception ex)
                {
                    LastErrorMessage = ex.Message;
                    Debug.LogWarning($"UgsInitializer: attempt {attempt + 1}/{MaxAttempts} failed - {ex.Message}");

                    bool isLastAttempt = attempt == MaxAttempts - 1;
                    if (isLastAttempt)
                    {
                        Status = UgsInitStatus.Failed;
                        return Status;
                    }

                    int delayMilliseconds = BaseRetryDelayMilliseconds * (1 << attempt);
                    await Task.Delay(delayMilliseconds);
                }
            }

            Status = UgsInitStatus.Failed;
            return Status;
        }
    }
}
