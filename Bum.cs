
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;
using System;
using System.Xml.Linq;
public class Bum : Bot
{// Direction variable: 1 = Clockwise, -1 = Counter-Clockwise
    private int _orbitDirection = 1;

    // The main method starts our bot
    static void Main(string[] args)
    {
        new Bum().Start();
    }

    // Called when a new round is started
    public override void Run()
    {
        // IMPORTANT: For orbital movement, we must decouple the parts.
        // If these are false, turning the body to avoid a wall will 
        // jerk the radar/gun to the side, breaking your lock.
        AdjustRadarForBodyTurn = true;
        AdjustGunForBodyTurn = true;
        AdjustRadarForGunTurn = true;

        // Start the radar spinning to find an enemy
        TurnRadarLeft(double.PositiveInfinity);

        // Repeat while the bot is running
        while (IsRunning)
        {
            Go();
        }
    }

    // We saw another bot -> Lock, Fire, and Move!
    public override void OnScannedBot(ScannedBotEvent e)
    {
        // --- 1. Radar Lock Logic ---

        // Get the angle to the enemy relative to the radar
        double bearing = RadarBearingTo(e.X, e.Y);

        // Calculate the extra scan width (Overshoot) to ensure we cover the enemy's width
        double spread = Math.Atan(36.0 / DistanceTo(e.X, e.Y)) * (180.0 / Math.PI);

        // Determine the turn amount. If bearing is positive (left), scan more left.
        double radarTurn = bearing + (bearing >= 0 ? spread : -spread);

        // Execute the radar turn immediately
        SetTurnRadarLeft(radarTurn);

        // --- 2. Firing Logic ---
        CalculateFiringSolution(e);

        // --- 3. Movement Logic (Orbital + Wall Smooth) ---
        CalculateOrbitalMovement(e);
    }

    // Abstracted Firing Logic
    private void CalculateFiringSolution(ScannedBotEvent e)
    {
        // Calculate the turn required to face the enemy coordinates

        double bulletSpeed = CalcBulletSpeed(0.5);

        // relative position (unit circle)
        double dx = e.X - X;
        double dy = e.Y - Y;

        // target velocity (unit circle)
        double vtx = e.Speed * Math.Cos(e.Direction * Math.PI / 180.0);
        double vty = e.Speed * Math.Sin(e.Direction * Math.PI / 180.0);

        // quadratic coefficients
        double A = (vtx * vtx + vty * vty) - (bulletSpeed * bulletSpeed);
        double B = 2 * (dx * vtx + dy * vty);
        double C = dx * dx + dy * dy;

        // discriminant
        double discriminant = B * B - 4 * A * C;
        if (discriminant < 0) return;

        double sqrtD = Math.Sqrt(discriminant);
        double t1 = (-B + sqrtD) / (2 * A);
        double t2 = (-B - sqrtD) / (2 * A);

        // pick smallest positive t
        double t = double.MaxValue;
        if (t1 > 0 && t1 < t) t = t1;
        if (t2 > 0 && t2 < t) t = t2;
        if (t == double.MaxValue) return;

        // intercept direction (unit circle)
        double ux = (dx + vtx * t) / (bulletSpeed * t);
        double uy = (dy + vty * t) / (bulletSpeed * t);

        // aim angle (unit circle)
        double aimAngle = Math.Atan2(uy, ux) * 180.0 / Math.PI;

        // compute shortest turn in unit-circle space
        double delta = aimAngle - GunDirection;
        delta = (delta + 180) % 360;
        if (delta < 0) delta += 360;
        delta -= 180;

        // apply turn (unit circle: positive = CCW)
        if (delta > 0)
            SetTurnGunLeft(delta);   // CCW
        else
            SetTurnGunRight(-delta); // CW

        if (GunHeat == 0)
            SetFire(0.5);

        // Set the gun to turn


    }

    // Abstracted Movement Logic
    private void CalculateOrbitalMovement(ScannedBotEvent e)
    {
        // 1. Get the absolute angle to the enemy (0-360 degrees)
        double angleToEnemy = DirectionTo(e.X, e.Y);

        // 2. Calculate the desired Orbital Angle.
        // If we add 90 degrees to the angleToEnemy, we will move perpendicular 
        // to them (a perfect circle).
        // _orbitDirection (1 or -1) determines if we orbit Clockwise or Counter-Clockwise.
        double goalDirection = angleToEnemy + (_orbitDirection * 90);

        // 3. Apply Wall Smoothing.
        // If moving at 'goalDirection' would make us hit a wall, this method
        // calculates a new, safe angle to glide along the wall instead.
        double smoothedDirection = WallSmooth(goalDirection);

        // 4. Calculate how much we need to turn the BODY to face this new direction.
        // CalcDeltaAngle handles the math to find the shortest turn (left or right).
        double turnAngle = CalcDeltaAngle(smoothedDirection, Direction);

        // 5. Execute commands
        SetTurnLeft(turnAngle);
        SetForward(100); // Always try to move full speed
    }

    // Wall Smoothing Algorithm (Whisker/Stick projection)
    private double WallSmooth(double goalAngle)
    {
        // Define the "Stick" length. This is how far ahead we look for walls.
        // 160 units is roughly 20 ticks of movement at max speed (8.0).
        double stickLength = 160;

        // Define a safety margin so we don't scrape the paint off the walls
        double margin = 20;

        // Loop to find a safe angle
        // We will test the 'goalAngle'. If it hits a wall, we rotate it slightly
        // and test again, repeating until we find an angle that fits in the arena.
        // We limit the loop to 25 iterations to prevent infinite freezing.
        for (int i = 0; i < 25; i++)
        {
            // 1. Convert the angle to Radians (Math.Cos/Sin require Radians)
            double angleRadians = goalAngle * (Math.PI / 180.0);

            // 2. Project the tip of the "stick" based on our current X,Y
            // Note: In Robocode/TankRoyale, 0 deg is East (Cos=1), 90 deg is North (Sin=1)
            double projectedX = X + (Math.Cos(angleRadians) * stickLength);
            double projectedY = Y + (Math.Sin(angleRadians) * stickLength);

            // 3. Check if that projected point is inside the arena boundaries
            bool safeX = projectedX > margin && projectedX < ArenaWidth - margin;
            bool safeY = projectedY > margin && projectedY < ArenaHeight - margin;

            if (safeX && safeY)
            {
                // The angle is safe! Return it.
                return goalAngle;
            }

            // 4. If not safe, rotate the angle slightly.
            // We rotate *against* the orbit direction to curve inward/away from the wall.
            // 5 degrees per iteration provides a smooth curve.
            goalAngle -= _orbitDirection * 5;
        }

        // If we fail to find a smooth angle, return the original (fallback)
        return goalAngle;
    }

    // If we hit a wall, reverse direction immediately so we don't get stuck
    public override void OnHitWall(HitWallEvent botHitWallEvent)
    {
        _orbitDirection = -_orbitDirection;
    }

    // If we get hit by a bullet, switch orbital direction to try and confuse the enemy's targeting
    public override void OnHitByBullet(HitByBulletEvent evt)
    {
        _orbitDirection = -_orbitDirection;
    }

    // If we crash into the enemy, switch direction to roll around them
    public override void OnHitBot(HitBotEvent botHitBotEvent)
    {
        _orbitDirection = -_orbitDirection;
    }
}