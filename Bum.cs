using Robocode.TankRoyale.BotApi;
using Robocode.TankRoyale.BotApi.Events;
using System;
using System.Collections.Generic;

public class Bum : Bot
{
    private const double WallOffset = 36;

    private bool _wasRammed;
    private double _lastEnemyEnergy = 100;
    private double _lastEnemyDirection;
    private bool _hasEnemyDirection;
    private int _surfDirection = 1;

    private readonly List<EnemyWave> _enemyWaves = new();

    private class EnemyWave
    {
        public double SourceX;
        public double SourceY;
        public double BulletSpeed;
        public int FireTurn;
    }

    static void Main(string[] args)
    {
        new Bum().Start();
    }

    public override void Run()
    {
        _enemyWaves.Clear();
        _lastEnemyEnergy = 100;
        _hasEnemyDirection = false;
        _wasRammed = false;

        AdjustRadarForBodyTurn = true;
        AdjustGunForBodyTurn = true;
        AdjustRadarForGunTurn = true;

        SetTurnRadarLeft(double.PositiveInfinity);

        while (IsRunning)
        {
            Go();
        }
    }

    public override void OnScannedBot(ScannedBotEvent e)
    {
        TrackEnemyWaves(e);

        double bearing = RadarBearingTo(e.X, e.Y);
        double spread = Math.Max(1.5, Math.Atan(36.0 / DistanceTo(e.X, e.Y)) * (180.0 / Math.PI));
        double radarTurn = bearing + (bearing >= 0 ? spread : -spread);
        SetTurnRadarLeft(radarTurn);

        CalculateFiringSolution(e);
        CalculateOrbitalMovement(e);
    }

    private void TrackEnemyWaves(ScannedBotEvent e)
    {
        double energyDrop = _lastEnemyEnergy - e.Energy;
        if (energyDrop >= 0.1 && energyDrop <= 3.0)
        {
            _enemyWaves.Add(new EnemyWave
            {
                SourceX = e.X,
                SourceY = e.Y,
                BulletSpeed = CalcBulletSpeed(energyDrop),
                FireTurn = TurnNumber
            });
        }

        _lastEnemyEnergy = e.Energy;

        _enemyWaves.RemoveAll(w =>
        {
            double traveled = (TurnNumber - w.FireTurn) * w.BulletSpeed;
            double distanceToMe = Math.Sqrt((X - w.SourceX) * (X - w.SourceX) + (Y - w.SourceY) * (Y - w.SourceY));
            return traveled > distanceToMe + 60;
        });
    }

    private void CalculateFiringSolution(ScannedBotEvent e)
    {
        double firePower = _wasRammed ? 3.0 : 1.0;
        double bulletSpeed = CalcBulletSpeed(firePower);

        double enemyHeading = e.Direction;
        double enemyTurnRate = 0;
        if (_hasEnemyDirection)
        {
            enemyTurnRate = NormalizeRelative(enemyHeading - _lastEnemyDirection);
            enemyTurnRate = Math.Clamp(enemyTurnRate, -10, 10);
        }

        _lastEnemyDirection = enemyHeading;
        _hasEnemyDirection = true;

        double predictedX = e.X;
        double predictedY = e.Y;
        double predictedHeading = enemyHeading;
        double predictedSpeed = e.Speed;

        for (int t = 1; t < 120; t++)
        {
            predictedHeading += enemyTurnRate;
            predictedX += Math.Cos(predictedHeading * Math.PI / 180.0) * predictedSpeed;
            predictedY += Math.Sin(predictedHeading * Math.PI / 180.0) * predictedSpeed;

            predictedX = Math.Clamp(predictedX, WallOffset, ArenaWidth - WallOffset);
            predictedY = Math.Clamp(predictedY, WallOffset, ArenaHeight - WallOffset);

            if (DistanceTo(predictedX, predictedY) <= bulletSpeed * t)
                break;
        }

        double aimAngle = DirectionTo(predictedX, predictedY);
        double delta = NormalizeRelative(aimAngle - GunDirection);

        if (delta > 0)
            SetTurnGunLeft(delta);
        else
            SetTurnGunRight(-delta);

        if (GunHeat == 0 && Math.Abs(GunBearingTo(predictedX, predictedY)) <= 3)
        {
            SetFire(firePower);
            _wasRammed = false;
        }
    }

    private void CalculateOrbitalMovement(ScannedBotEvent e)
    {
        EnemyWave surfWave = GetClosestWave();
        double enemyBearing = DirectionTo(e.X, e.Y);

        int direction = _surfDirection;
        if (surfWave != null)
        {
            double leftDanger = PredictWaveDanger(surfWave, enemyBearing, -1);
            double rightDanger = PredictWaveDanger(surfWave, enemyBearing, 1);
            direction = leftDanger < rightDanger ? -1 : 1;
        }

        _surfDirection = direction;

        double goalDirection = WallSmoothing(enemyBearing + (90 * direction), direction);
        double turnAngle = CalcDeltaAngle(goalDirection, Direction);

        SetTurnLeft(turnAngle);
        SetForward(100);
    }

    private EnemyWave GetClosestWave()
    {
        EnemyWave closest = null;
        double closestDistance = double.MaxValue;

        foreach (var wave in _enemyWaves)
        {
            double traveled = (TurnNumber - wave.FireTurn) * wave.BulletSpeed;
            double distance = Math.Sqrt((X - wave.SourceX) * (X - wave.SourceX) + (Y - wave.SourceY) * (Y - wave.SourceY));
            double distanceToImpact = distance - traveled;

            if (distanceToImpact > 0 && distanceToImpact < closestDistance)
            {
                closestDistance = distanceToImpact;
                closest = wave;
            }
        }

        return closest;
    }

    private double PredictWaveDanger(EnemyWave wave, double enemyBearing, int direction)
    {
        double px = X;
        double py = Y;
        double heading = Direction;
        double velocity = Speed;

        for (int i = 0; i < 40; i++)
        {
            double moveAngle = WallSmoothing(enemyBearing + (90 * direction), direction, px, py);
            double turn = NormalizeRelative(moveAngle - heading);
            double maxTurn = 10 - 0.75 * Math.Abs(velocity);
            turn = Math.Clamp(turn, -maxTurn, maxTurn);
            heading += turn;

            velocity = Math.Clamp(velocity + 1, -8, 8);
            px += Math.Cos(heading * Math.PI / 180.0) * velocity;
            py += Math.Sin(heading * Math.PI / 180.0) * velocity;

            px = Math.Clamp(px, WallOffset, ArenaWidth - WallOffset);
            py = Math.Clamp(py, WallOffset, ArenaHeight - WallOffset);

            double traveled = (TurnNumber - wave.FireTurn + i + 1) * wave.BulletSpeed;
            double distFromSource = Math.Sqrt((px - wave.SourceX) * (px - wave.SourceX) + (py - wave.SourceY) * (py - wave.SourceY));
            if (traveled >= distFromSource - 18)
            {
                double wallDanger = 1.0 / (Math.Min(Math.Min(px - WallOffset, ArenaWidth - WallOffset - px), Math.Min(py - WallOffset, ArenaHeight - WallOffset - py)) + 1);
                double enemyDanger = 1.0 / (DistanceTo(px, py, wave.SourceX, wave.SourceY) + 1);
                return wallDanger * 3 + enemyDanger;
            }
        }

        return 1;
    }

    private double WallSmoothing(double angle, int direction)
    {
        return WallSmoothing(angle, direction, X, Y);
    }

    private double WallSmoothing(double angle, int direction, double fromX, double fromY)
    {
        double smoothed = angle;
        for (int i = 0; i < 40; i++)
        {
            double testX = fromX + Math.Cos(smoothed * Math.PI / 180.0) * 120;
            double testY = fromY + Math.Sin(smoothed * Math.PI / 180.0) * 120;

            if (testX > WallOffset && testX < ArenaWidth - WallOffset && testY > WallOffset && testY < ArenaHeight - WallOffset)
                break;

            smoothed += direction * 4;
        }

        return smoothed;
    }

    private static double NormalizeRelative(double angle)
    {
        angle = (angle + 180) % 360;
        if (angle < 0) angle += 360;
        return angle - 180;
    }

    private static double DistanceTo(double x1, double y1, double x2, double y2)
    {
        return Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
    }

    public override void OnHitWall(HitWallEvent botHitWallEvent)
    {
        _surfDirection = -_surfDirection;
    }

    public override void OnHitBot(HitBotEvent botHitBotEvent)
    {
        _wasRammed = true;
        _surfDirection = -_surfDirection;
    }
}
