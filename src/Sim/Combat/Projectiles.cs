using StickFighter.Sim.Characters;
using StickFighter.Sim.Collision;
using StickFighter.Sim.Numerics;

namespace StickFighter.Sim.Combat;

// Fireballs in flight. A move with a Spawn (the fireball) throws one from its hand on its spawn
// frame, forward at its strength's speed; one per player on screen. Each frame it flies on, and it
// dies off the stage or once it's further from its thrower than a screen. Hits, blocks and two
// fireballs meeting are contacts (Hits), which spend it.
public static class Projectiles
{
    public static bool HasOneOut(in GameState state, int player)
    {
        for (var slot = 0; slot < GameState.MaxProjectiles; slot++)
        {
            if (state.Projectiles[slot].Active && state.Projectiles[slot].Owner == player)
                return true;
        }
        return false;
    }

    // A frame of flight, then any new fireballs thrown this frame (they appear at the hand).
    public static void Step(ref GameState state)
    {
        for (var slot = 0; slot < GameState.MaxProjectiles; slot++)
        {
            ref var projectile = ref state.Projectiles[slot];
            if (!projectile.Active)
                continue;
            projectile.Position += projectile.Velocity;
            projectile.Frame++;
            var owner = state.Fighters[projectile.Owner].Position.X;
            var offStage = projectile.Position.X < state.Stage.LeftWall || projectile.Position.X > state.Stage.RightWall;
            var offScreen = Fix.Abs(projectile.Position.X - owner) > state.Stage.MaxSeparation;
            if (offStage || offScreen)
                projectile.Active = false;
        }

        for (var player = 0; player < GameState.PlayerCount; player++)
            Throw(ref state, player);
    }

    private static void Throw(ref GameState state, int player)
    {
        ref readonly var fighter = ref state.Fighters[player];
        if (Body.MoveOf(fighter) is not { Spawn: { } spawn } || fighter.ActionFrame != spawn.Frame || HasOneOut(state, player))
            return;

        for (var slot = 0; slot < GameState.MaxProjectiles; slot++)
        {
            if (state.Projectiles[slot].Active)
                continue;
            var stats = Characters.Characters.Placeholder.Stats;
            var speed = fighter.SpecialStrength switch
            {
                0 => stats.ProjectileSpeedLight,
                1 => stats.ProjectileSpeedMedium,
                _ => stats.ProjectileSpeedHeavy,
            };
            state.Projectiles[slot] = new ProjectileState
            {
                Active = true,
                Owner = player,
                Position = Body.Pose(fighter)[spawn.From],
                Velocity = new FixVec2(speed * fighter.Facing, Fix.Zero),
                Frame = 0,
                Move = fighter.Move,
            };
            return;
        }
    }
}
