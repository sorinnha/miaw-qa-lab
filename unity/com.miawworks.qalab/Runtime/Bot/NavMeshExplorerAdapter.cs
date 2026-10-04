#if QALAB_AI
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Built-in bot <c>navmesh_explorer</c> (spec 01): walks to random reachable points on the baked
    /// NavMesh and sometimes interacts with what is in range. It needs only a baked NavMesh and a
    /// registered <see cref="IBotMover"/>, no game-specific code, which is why it finds level problems
    /// (holes, gaps, slow zones) that scripted tests never visit.
    /// <list type="bullet">
    /// <item>New target: a random point inside the NavMesh bounds, snapped with
    /// <c>NavMesh.SamplePosition(.., 5 m)</c>, used only if <c>CalculatePath</c> is <c>PathComplete</c>.</item>
    /// <item>Follows the path corners with <c>MoveTowards</c>, advancing within 0.5 m of a corner.</item>
    /// <item>Gives up after path length / speed × 2 + 3 s and picks a new target.</item>
    /// <item>With p = 0.2 per step it tries to interact; a success is logged as <c>interact</c>.</item>
    /// </list>
    /// All choices come from <see cref="BotContext.Random"/>, so a seed replays the same decisions.
    /// </summary>
    public sealed class NavMeshExplorerAdapter : IBotAdapter
    {
        public const string AdapterName = "navmesh_explorer";
        public const float CornerReachedM = 0.5f;
        public const float SampleRadiusM = 5f;
        public const double InteractChance = 0.2;
        private const int TargetAttemptsPerStep = 10;

        private readonly float _speedMps;
        private readonly NavMeshPath _path = new NavMeshPath();
        private Vector3[] _corners = Array.Empty<Vector3>();
        private int _corner;
        private float _giveUpAt;
        private float _lastStepAt;
        private Bounds _bounds;
        private bool _hasBounds;
        private int _boundsScene = -1;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        /// <param name="speedMps">The player's walking speed, used for the give-up time. Games register
        /// their own: <c>BotAdapterRegistry.Register("navmesh_explorer", () => new NavMeshExplorerAdapter(6f))</c>.</param>
        public NavMeshExplorerAdapter(float speedMps = 4.5f)
        {
            if (!(speedMps > 0f)) throw new ArgumentOutOfRangeException(nameof(speedMps));
            _speedMps = speedMps;
        }

        public string Name => AdapterName;

        public void Begin(BotContext ctx)
        {
            _corners = Array.Empty<Vector3>();
            _hasLastPosition = false;
        }

        public BotStepResult Step(BotContext ctx)
        {
            var now = Time.unscaledTime;
            var player = ctx.Player;
            var mover = ctx.Mover;
            if (player == null || mover == null)
            {
                return BotStepResult.Continue;   // the game hasn't registered its player yet
            }
            var position = player.position;
            if (Teleported(position, now))
            {
                _corners = Array.Empty<Vector3>();   // respawned: the old path starts somewhere else
            }
            _lastPosition = position;
            _lastStepAt = now;
            _hasLastPosition = true;

            if (ctx.Random.Chance(InteractChance) && mover.TryInteract(out var objectName))
            {
                ctx.LogAction("interact", null, null, new JObject { ["object"] = objectName });
            }

            if (_corner >= _corners.Length || now >= _giveUpAt)
            {
                if (!PickTarget(ctx, position, now))
                {
                    mover.Stop();
                    return BotStepResult.Continue;   // no NavMesh here (yet): try again next step
                }
            }
            while (_corner < _corners.Length && Flat(_corners[_corner] - position).magnitude <= CornerReachedM)
            {
                _corner++;
            }
            if (_corner < _corners.Length)
            {
                mover.MoveTowards(_corners[_corner]);
            }
            else
            {
                mover.Stop();
            }
            return BotStepResult.Continue;
        }

        public void End(BotContext ctx) => ctx.Mover?.Stop();

        private bool PickTarget(BotContext ctx, Vector3 from, float now)
        {
            if (!EnsureBounds()) return false;
            if (!NavMesh.SamplePosition(from, out var start, SampleRadiusM, NavMesh.AllAreas)) return false;
            for (var attempt = 0; attempt < TargetAttemptsPerStep; attempt++)
            {
                var random = new Vector3(
                    ctx.Random.Range(_bounds.min.x, _bounds.max.x),
                    ctx.Random.Range(_bounds.min.y, _bounds.max.y),
                    ctx.Random.Range(_bounds.min.z, _bounds.max.z));
                if (!NavMesh.SamplePosition(random, out var hit, SampleRadiusM, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, _path)) continue;
                if (_path.status != NavMeshPathStatus.PathComplete || _path.corners.Length < 2) continue;

                _corners = _path.corners;
                _corner = 1;   // corner 0 is where the player stands
                var length = 0f;
                for (var i = 1; i < _corners.Length; i++) length += Vector3.Distance(_corners[i - 1], _corners[i]);
                _giveUpAt = now + length / _speedMps * 2f + 3f;
                ctx.LogAction("move_to", hit.position);
                return true;
            }
            return false;
        }

        // The NavMesh of the active scene, measured once per scene: its triangles' bounding box.
        private bool EnsureBounds()
        {
            var scene = SceneManager.GetActiveScene().handle;
            if (_hasBounds && scene == _boundsScene) return true;
            var mesh = NavMesh.CalculateTriangulation();
            _hasBounds = mesh.vertices.Length > 0;
            _boundsScene = scene;
            if (!_hasBounds) return false;
            _bounds = new Bounds(mesh.vertices[0], Vector3.zero);
            foreach (var v in mesh.vertices) _bounds.Encapsulate(v);
            return true;
        }

        // Farther across the ground than walking explains since the last step: a respawn or a scripted
        // teleport. Ground distance only, so falling fast is not mistaken for a teleport.
        private bool Teleported(Vector3 position, float now) =>
            _hasLastPosition && Flat(position - _lastPosition).magnitude > _speedMps * (now - _lastStepAt) * 3f + 2f;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
#endif
