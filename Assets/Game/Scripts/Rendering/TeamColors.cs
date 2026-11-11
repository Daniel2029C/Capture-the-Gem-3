using CTG.Combat;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>The colour each team is drawn in (presentation only; the combat rules never use it).</summary>
    public static class TeamColors
    {
        public static Color For(Team team) => team switch
        {
            Team.Red => new Color32(225, 55, 55, 255),
            Team.Blue => new Color32(60, 110, 255, 255),
            Team.Monster => new Color32(160, 80, 200, 255),
            _ => new Color32(70, 70, 80, 255),
        };
    }
}
