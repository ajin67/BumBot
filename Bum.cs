
using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;
using System;
public class Bum : Bot
{// Direction variable: 1 = Clockwise, -1 = Counter-Clockwise
    private bool _wasRammed;
    private const double WallOffset = 36;
    private bool _cornerChosen;
    private double _cornerX;
    private double _cornerY;

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
        SetTurnRadarLeft(double.PositiveInfinity);

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

        // Calculate a dynamic lock width with a tiny floor to keep the lock stable
        double spread = Math.Max(1.5, Math.Atan(36.0 / DistanceTo(e.X, e.Y)) * (180.0 / Math.PI));

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

        double firePower = _wasRammed ? 3.0 : 1.0;
        double bulletSpeed = CalcBulletSpeed(firePower);

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

        if (GunHeat == 0 && Math.Abs(GunBearingTo(e.X, e.Y)) <= 3)
        {
            SetFire(firePower);
            _wasRammed = false;
        }

        // Set the gun to turn


    }

    // Abstracted Movement Logic
    private void CalculateOrbitalMovement(ScannedBotEvent e)
    {
        // Move to the nearest inset corner, then stay parked there.
        double minX = WallOffset;
        double minY = WallOffset;
        double maxX = ArenaWidth - WallOffset;
        double maxY = ArenaHeight - WallOffset;

        if (!_cornerChosen)
        {
            double[,] corners =
            {
                { minX, minY },
                { minX, maxY },
                { maxX, minY },
                { maxX, maxY }
            };

            double bestDistance = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                double cx = corners[i, 0];
                double cy = corners[i, 1];
                double distance = DistanceTo(cx, cy);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    _cornerX = cx;
                    _cornerY = cy;
                }
            }

            _cornerChosen = true;
        }

        double cornerDistance = DistanceTo(_cornerX, _cornerY);
        if (cornerDistance > 20)
        {
            double goalDirection = DirectionTo(_cornerX, _cornerY);
            double turnAngle = CalcDeltaAngle(goalDirection, Direction);

            SetTurnLeft(turnAngle);
            SetForward(Math.Min(100, cornerDistance));
        }
        else
        {
            // Hold this inset corner and keep scanning/firing.
            SetForward(0);
        }
    }

    // If we hit a wall, reverse direction immediately so we don't get stuck
    public override void OnHitWall(HitWallEvent botHitWallEvent)
    {
        _cornerChosen = false;
    }

    // If we get hit by a bullet, switch orbital direction to try and confuse the enemy's targeting
    public override void OnHitByBullet(HitByBulletEvent evt)
    {
    }

    // If we crash into the enemy, switch direction to roll around them
    public override void OnHitBot(HitBotEvent botHitBotEvent)
    {
        _wasRammed = true;
    }
}
