using TTTXO.Game.UI;
using UnityEngine;

namespace TTTXO.Game.Bootstrap
{
    /// <summary>
    /// Entry point placed on the persistent root GameObject. Milestone 1 only simulates a splash
    /// load and hands off to the <see cref="ScreenRouter"/>; real Unity Gaming Services
    /// initialization (Authentication, Remote Config, etc.) is out of scope until a later
    /// milestone (see Docs/03-Arquitectura-UGS-TicTacToe.md).
    /// </summary>
    [RequireComponent(typeof(GameManager))]
    [RequireComponent(typeof(ScreenRouter))]
    public class AppBootstrap : MonoBehaviour
    {
        private void Start()
        {
            var router = GetComponent<ScreenRouter>();
            router.Initialize();
        }
    }
}
