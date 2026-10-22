using System;
using BepInEx.Logging;
using UnityEngine;

// Attached only to the Orange prefab clone created by BallSpawnService.
public sealed class InfernoBall : MonoBehaviour
{
    private InfernoParameters _parameters;
    private ManualLogSource? _log;
    private bool _detonated;
    private bool _capacityWarningLogged;

    public void Initialize(InfernoParameters parameters, ManualLogSource log)
    {
        _parameters = parameters;
        _log = log;
        name = "Inferno Ball";

        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null)
            sprite.color = new Color(1f, 0.34f, 0.07f, 1f);

        InfernoExplosionVFX.AttachAura(gameObject, log);
        _log.LogInfo("Inferno Ball initialized on original Orange clone; native physics preserved");
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (_detonated || _log == null || collision.gameObject == null ||
            !collision.gameObject.CompareTag("Tile"))
            return;

        try
        {
            PhysicsTile physicsTile = collision.gameObject.GetComponent<PhysicsTile>();
            WorldManager world = WorldManager.Instance;
            if (physicsTile == null || world == null || world.isMainMenu || world.worldTiles == null)
                return;

            Vector2Int cell = physicsTile.worldCoordinates;
            Tile[,] tiles = world.worldTiles;
            if (cell.x < 0 || cell.y < 0 || cell.x >= tiles.GetLength(0) ||
                cell.y >= tiles.GetLength(1))
                return;

            Tile tile = tiles[cell.x, cell.y];
            if (!InfernoExplosion.IsOrdinaryDestructible(tile))
                return;

            Vector2 impact = collision.contactCount > 0
                ? collision.GetContact(0).point
                : new Vector2(cell.x - world.worldSize / 2 + 0.5f,
                    cell.y - world.worldSize / 2 + 0.5f);
            PixelParticipant? owner = GetComponent<PixelBallOwner>()?.Participant;
            if (!InfernoExplosion.TryCreate(world, impact, _parameters, _log, owner,
                out string reason))
            {
                if (!_capacityWarningLogged)
                {
                    _log.LogWarning($"Inferno impact deferred: {reason}");
                    _capacityWarningLogged = true;
                }
                return;
            }

            _detonated = true;
            _log.LogInfo($"Inferno Ball valid impact: cell={cell}, contact={impact}; " +
                "detonating once and destroying original clone");
            Destroy(gameObject); // Native Ball.OnDestroy removes BallManager.ballList entry.
        }
        catch (Exception exception)
        {
            _log.LogError($"Inferno Ball collision failed safely: {exception}");
        }
    }
}
