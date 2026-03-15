using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GlobalEnums;
using UnityEngine;

namespace DebugMod.Hitbox
{
    public class HitboxRender : MonoBehaviour
    {
        private struct FrameStats
        {
            public int VisitedColliders;
            public int DrawnColliders;
            public int NullColliders;
            public int InactiveColliders;
            public int VisibilityCulledColliders;
            public int Boxes;
            public int Edges;
            public int PolygonPaths;
            public int Circles;
            public int LineSegments;
            public int CircleSegments;
            public long ElapsedTicks;

            public int TotalSegments => LineSegments + CircleSegments;
        }

        private struct AggregateStats
        {
            public int FrameCount;
            public long TotalElapsedTicks;
            public long MaxElapsedTicks;
            public int MaxDrawnColliders;
            public int MaxNullColliders;
            public int MaxInactiveColliders;
            public int MaxVisibilityCulledColliders;
            public int MaxSegments;
        }

        // ReSharper disable once StructCanBeMadeReadOnly
        private struct HitboxType: IComparable<HitboxType>
        {
            public static readonly HitboxType Knight = new(Color.yellow, 0);                     // yellow
            public static readonly HitboxType Enemy = new(new Color(0.8f, 0, 0), 1);       // red      
            public static readonly HitboxType Attack = new(Color.cyan, 2);                       // cyan
            public static readonly HitboxType Terrain = new(new Color(0, 0.8f, 0), 3);     // green
            public static readonly HitboxType Trigger = new(new Color(0.5f, 0.5f, 1f), 4); // blue
            public static readonly HitboxType Breakable = new(new Color(1f, 0.75f, 0.8f), 5); // pink
            public static readonly HitboxType Gate = new(new Color(0.0f, 0.0f, 0.5f), 6); // dark blue
            public static readonly HitboxType HazardRespawn = new(new Color(0.5f, 0.0f, 0.5f),7); // purple 
            public static readonly HitboxType Other = new(new Color(0.9f, 0.6f, 0.4f), 8); // orange

            
            public readonly Color Color;
            public readonly int Depth;

            private HitboxType(Color color, int depth)
            {
                Color = color;
                Depth = depth;
            }

            public int CompareTo(HitboxType other)
            {
                return other.Depth.CompareTo(Depth);
            }
        }

        private readonly SortedDictionary<HitboxType, HashSet<Collider2D>> colliders = new()
        {
            {HitboxType.Knight, new HashSet<Collider2D>()},
            {HitboxType.Enemy, new HashSet<Collider2D>()},
            {HitboxType.Attack, new HashSet<Collider2D>()},
            {HitboxType.Terrain, new HashSet<Collider2D>()},
            {HitboxType.Trigger, new HashSet<Collider2D>()},
            {HitboxType.Breakable, new HashSet<Collider2D>()},
            {HitboxType.Gate, new HashSet<Collider2D>()},
            {HitboxType.HazardRespawn, new HashSet<Collider2D>()},
            {HitboxType.Other, new HashSet<Collider2D>()},
        };
        private readonly SortedDictionary<HitboxType, HashSet<Collider2D>> activeColliders = new()
        {
            {HitboxType.Knight, new HashSet<Collider2D>()},
            {HitboxType.Enemy, new HashSet<Collider2D>()},
            {HitboxType.Attack, new HashSet<Collider2D>()},
            {HitboxType.Terrain, new HashSet<Collider2D>()},
            {HitboxType.Trigger, new HashSet<Collider2D>()},
            {HitboxType.Breakable, new HashSet<Collider2D>()},
            {HitboxType.Gate, new HashSet<Collider2D>()},
            {HitboxType.HazardRespawn, new HashSet<Collider2D>()},
            {HitboxType.Other, new HashSet<Collider2D>()},
        };
        private readonly List<Vector2> polygonPointBuffer = new();
        private FrameStats lastFrameStats;
        private AggregateStats aggregateStats;
        private float lastActiveRefreshRealtime;

        private const float ActiveRefreshIntervalSeconds = 0.05f;

        public static float LineWidth => Math.Max(0.7f, Screen.width / 960f * GameCameras.instance.tk2dCam.ZoomFactor);

        private void Start()
        {
            foreach (Collider2D col in Resources.FindObjectsOfTypeAll<Collider2D>())
            {
                TryAddHitboxes(col);
            }

            RefreshActiveColliderCache(force: true);
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup - lastActiveRefreshRealtime >= ActiveRefreshIntervalSeconds)
            {
                RefreshActiveColliderCache(force: false);
            }
        }

        public void UpdateHitbox(GameObject go)
        {
            foreach (Collider2D col in go.GetComponentsInChildren<Collider2D>(true))
            {
                TryAddHitboxes(col);
            }
        }

        private Vector2 LocalToScreenPoint(Camera camera, Collider2D collider2D, Vector2 point)
        {
            Vector2 result = camera.WorldToScreenPoint((Vector2)collider2D.transform.TransformPoint(point + collider2D.offset));
            return new Vector2((int) Math.Round(result.x), (int) Math.Round(Screen.height - result.y));
        }

        private void TryAddHitboxes(Collider2D collider2D)
        {
            if (collider2D == null)
            {
                return;
            }

            if (collider2D is BoxCollider2D or PolygonCollider2D or EdgeCollider2D or CircleCollider2D)
            {
                GameObject go = collider2D.gameObject;
                if (collider2D.GetComponent<DamageHero>() || collider2D.gameObject.LocateMyFSM("damages_hero"))
                {
                    AddTrackedCollider(HitboxType.Enemy, collider2D);
                } 
                else if (go.GetComponent<HealthManager>()||go.LocateMyFSM("health_manager_enemy") || go.LocateMyFSM("health_manager"))
                {
                    AddTrackedCollider(HitboxType.Other, collider2D);
                } 
                else if (go.layer == (int) PhysLayers.TERRAIN)
                {
                    if (go.name.Contains("Breakable") || go.name.Contains("Collapse") || go.GetComponent<Breakable>() != null) AddTrackedCollider(HitboxType.Breakable, collider2D);
                    else AddTrackedCollider(HitboxType.Terrain, collider2D);
                } 
                else if (go == HeroController.instance?.gameObject && !collider2D.isTrigger)
                {
                    AddTrackedCollider(HitboxType.Knight, collider2D);
                } 
                else if (go.GetComponent<DamageEnemies>()||go.LocateMyFSM("damages_enemy") || go.name == "Damager" && go.LocateMyFSM("Damage"))
                {
                    AddTrackedCollider(HitboxType.Attack, collider2D);
                } 
                else if (collider2D.isTrigger && collider2D.GetComponent<HazardRespawnTrigger>())
                {
                    AddTrackedCollider(HitboxType.HazardRespawn, collider2D);
                } 
                else if (collider2D.isTrigger && collider2D.GetComponent<TransitionPoint>())
                {
                    AddTrackedCollider(HitboxType.Gate, collider2D);
                } 
                else if (collider2D.GetComponent<Breakable>())
                {
                    NonBouncer bounce = collider2D.GetComponent<NonBouncer>();
                    if (bounce == null || !bounce.active)
                    {
                        AddTrackedCollider(HitboxType.Trigger, collider2D);
                    }
                } 
                else if (HitboxViewer.State == 2)
                {
                    AddTrackedCollider(HitboxType.Other, collider2D);
                }
            }
        }

        private void OnGUI()
        {
            if (Event.current?.type != EventType.Repaint || Camera.main == null || GameManager.instance == null || GameManager.instance.isPaused)
            {
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            FrameStats frameStats = default;
            GUI.depth = int.MaxValue;
            Camera camera = Camera.main;
            float lineWidth = LineWidth;
            foreach (var pair in activeColliders)
            {
                bool needsPrune = false;
                foreach (Collider2D collider2D in pair.Value)
                {
                    frameStats.VisitedColliders++;
                    DrawHitbox(camera, collider2D, pair.Key, lineWidth, ref frameStats);
                    if (collider2D == null || !collider2D.isActiveAndEnabled)
                    {
                        needsPrune = true;
                    }
                }

                if (needsPrune)
                {
                    pair.Value.RemoveWhere(static collider2D => collider2D == null || !collider2D.isActiveAndEnabled);
                }
            }

            stopwatch.Stop();
            frameStats.ElapsedTicks = stopwatch.ElapsedTicks;
            lastFrameStats = frameStats;
            aggregateStats.FrameCount++;
            aggregateStats.TotalElapsedTicks += frameStats.ElapsedTicks;
            aggregateStats.MaxElapsedTicks = Math.Max(aggregateStats.MaxElapsedTicks, frameStats.ElapsedTicks);
            aggregateStats.MaxDrawnColliders = Math.Max(aggregateStats.MaxDrawnColliders, frameStats.DrawnColliders);
            aggregateStats.MaxNullColliders = Math.Max(aggregateStats.MaxNullColliders, frameStats.NullColliders);
            aggregateStats.MaxInactiveColliders = Math.Max(aggregateStats.MaxInactiveColliders, frameStats.InactiveColliders);
            aggregateStats.MaxVisibilityCulledColliders = Math.Max(aggregateStats.MaxVisibilityCulledColliders, frameStats.VisibilityCulledColliders);
            aggregateStats.MaxSegments = Math.Max(aggregateStats.MaxSegments, frameStats.TotalSegments);
        }

        private void DrawHitbox(Camera camera, Collider2D collider2D, HitboxType hitboxType, float lineWidth, ref FrameStats frameStats)
        {
            if (collider2D == null)
            {
                frameStats.NullColliders++;
                return;
            }

            if (!collider2D.isActiveAndEnabled)
            {
                frameStats.InactiveColliders++;
                return;
            }

            if (!IsColliderVisible(camera, collider2D))
            {
                frameStats.VisibilityCulledColliders++;
                return;
            }

            frameStats.DrawnColliders++;
            int origDepth = GUI.depth;
            GUI.depth = hitboxType.Depth;
            if (collider2D is BoxCollider2D or EdgeCollider2D or PolygonCollider2D)
            {
                switch (collider2D)
                {
                    case BoxCollider2D boxCollider2D:
                        frameStats.Boxes++;
                        DrawBox(boxCollider2D, camera, hitboxType, lineWidth, ref frameStats);
                        break;
                    case EdgeCollider2D edgeCollider2D:
                        frameStats.Edges++;
                        DrawPointSequence(edgeCollider2D.points, edgeCollider2D.pointCount, camera, collider2D, hitboxType, lineWidth, closeLoop: false, ref frameStats);
                        break;
                    case PolygonCollider2D polygonCollider2D:
                        for (int i = 0; i < polygonCollider2D.pathCount; i++)
                        {
                            frameStats.PolygonPaths++;
                            polygonPointBuffer.Clear();
                            polygonCollider2D.GetPath(i, polygonPointBuffer);
                            DrawPointSequence(polygonPointBuffer, polygonPointBuffer.Count, camera, collider2D, hitboxType, lineWidth, closeLoop: true, ref frameStats);
                        }
                        break;
                }
            } 
            else if (collider2D is CircleCollider2D circleCollider2D)
            {
                frameStats.Circles++;
                Vector2 center = LocalToScreenPoint(camera, collider2D, Vector2.zero);
                Vector2 right = LocalToScreenPoint(camera, collider2D, Vector2.right * circleCollider2D.radius);
                int radius = (int) Math.Round(Vector2.Distance(center, right));
                int segmentsPerQuarter = Mathf.Clamp(radius / 16, 4, 32);
                frameStats.CircleSegments += segmentsPerQuarter * 4;
                Drawing.DrawCircle(center, radius, hitboxType.Color, lineWidth, true, segmentsPerQuarter);
            }

            GUI.depth = origDepth;
        }

        private void DrawBox(BoxCollider2D boxCollider2D, Camera camera, HitboxType hitboxType, float lineWidth, ref FrameStats frameStats)
        {
            Vector2 halfSize = boxCollider2D.size / 2f;
            Vector2 topLeft = new(-halfSize.x, halfSize.y);
            Vector2 topRight = halfSize;
            Vector2 bottomRight = new(halfSize.x, -halfSize.y);
            Vector2 bottomLeft = -halfSize;

            DrawLineSegment(camera, boxCollider2D, topLeft, topRight, hitboxType, lineWidth, ref frameStats);
            DrawLineSegment(camera, boxCollider2D, topRight, bottomRight, hitboxType, lineWidth, ref frameStats);
            DrawLineSegment(camera, boxCollider2D, bottomRight, bottomLeft, hitboxType, lineWidth, ref frameStats);
            DrawLineSegment(camera, boxCollider2D, bottomLeft, topLeft, hitboxType, lineWidth, ref frameStats);
        }

        private void DrawPointSequence(IReadOnlyList<Vector2> points, int pointCount, Camera camera, Collider2D collider2D, HitboxType hitboxType, float lineWidth, bool closeLoop, ref FrameStats frameStats)
        {
            if (pointCount < 2)
            {
                return;
            }

            for (int i = 0; i < pointCount - 1; i++)
            {
                DrawLineSegment(camera, collider2D, points[i], points[i + 1], hitboxType, lineWidth, ref frameStats);
            }

            if (closeLoop)
            {
                DrawLineSegment(camera, collider2D, points[pointCount - 1], points[0], hitboxType, lineWidth, ref frameStats);
            }
        }

        private void DrawLineSegment(Camera camera, Collider2D collider2D, Vector2 start, Vector2 end, HitboxType hitboxType, float lineWidth, ref FrameStats frameStats)
        {
            Vector2 pointA = LocalToScreenPoint(camera, collider2D, start);
            Vector2 pointB = LocalToScreenPoint(camera, collider2D, end);
            frameStats.LineSegments++;
            Drawing.DrawLine(pointA, pointB, hitboxType.Color, lineWidth, true);
        }

        public string[] GetStatusLines()
        {
            int trackedKnight = colliders[HitboxType.Knight].Count;
            int trackedEnemy = colliders[HitboxType.Enemy].Count;
            int trackedAttack = colliders[HitboxType.Attack].Count;
            int trackedTerrain = colliders[HitboxType.Terrain].Count;
            int trackedTrigger = colliders[HitboxType.Trigger].Count;
            int trackedBreakable = colliders[HitboxType.Breakable].Count;
            int trackedGate = colliders[HitboxType.Gate].Count;
            int trackedHazardRespawn = colliders[HitboxType.HazardRespawn].Count;
            int trackedOther = colliders[HitboxType.Other].Count;
            int activeKnight = activeColliders[HitboxType.Knight].Count;
            int activeEnemy = activeColliders[HitboxType.Enemy].Count;
            int activeAttack = activeColliders[HitboxType.Attack].Count;
            int activeTerrain = activeColliders[HitboxType.Terrain].Count;
            int activeTrigger = activeColliders[HitboxType.Trigger].Count;
            int activeBreakable = activeColliders[HitboxType.Breakable].Count;
            int activeGate = activeColliders[HitboxType.Gate].Count;
            int activeHazardRespawn = activeColliders[HitboxType.HazardRespawn].Count;
            int activeOther = activeColliders[HitboxType.Other].Count;
            int trackedTotal = trackedKnight + trackedEnemy + trackedAttack + trackedTerrain + trackedTrigger + trackedBreakable + trackedGate + trackedHazardRespawn + trackedOther;
            int activeTotal = activeKnight + activeEnemy + activeAttack + activeTerrain + activeTrigger + activeBreakable + activeGate + activeHazardRespawn + activeOther;
            double lastFrameMs = StopwatchTicksToMs(lastFrameStats.ElapsedTicks);
            double avgFrameMs = aggregateStats.FrameCount > 0 ? StopwatchTicksToMs(aggregateStats.TotalElapsedTicks / aggregateStats.FrameCount) : 0d;
            double maxFrameMs = StopwatchTicksToMs(aggregateStats.MaxElapsedTicks);

            StringBuilder trackedByType = new StringBuilder();
            trackedByType.Append("knight=").Append(trackedKnight)
                .Append(" enemy=").Append(trackedEnemy)
                .Append(" attack=").Append(trackedAttack)
                .Append(" terrain=").Append(trackedTerrain)
                .Append(" trigger=").Append(trackedTrigger)
                .Append(" breakable=").Append(trackedBreakable)
                .Append(" gate=").Append(trackedGate)
                .Append(" hazard=").Append(trackedHazardRespawn)
                .Append(" other=").Append(trackedOther);
            StringBuilder activeByType = new StringBuilder();
            activeByType.Append("knight=").Append(activeKnight)
                .Append(" enemy=").Append(activeEnemy)
                .Append(" attack=").Append(activeAttack)
                .Append(" terrain=").Append(activeTerrain)
                .Append(" trigger=").Append(activeTrigger)
                .Append(" breakable=").Append(activeBreakable)
                .Append(" gate=").Append(activeGate)
                .Append(" hazard=").Append(activeHazardRespawn)
                .Append(" other=").Append(activeOther);

            return new[]
            {
                $"Hitbox status: tracked_total={trackedTotal} active_total={activeTotal} visited_last={lastFrameStats.VisitedColliders} drawn_last={lastFrameStats.DrawnColliders} null_last={lastFrameStats.NullColliders} inactive_last={lastFrameStats.InactiveColliders} culled_last={lastFrameStats.VisibilityCulledColliders}",
                $"Hitbox tracked by type: {trackedByType}",
                $"Hitbox active by type: {activeByType}",
                $"Hitbox primitives last: boxes={lastFrameStats.Boxes} edges={lastFrameStats.Edges} polygon_paths={lastFrameStats.PolygonPaths} circles={lastFrameStats.Circles} line_segments={lastFrameStats.LineSegments} circle_segments={lastFrameStats.CircleSegments} total_segments={lastFrameStats.TotalSegments}",
                $"Hitbox timing: frames={aggregateStats.FrameCount} frame_ms_last={lastFrameMs:F3} frame_ms_avg={avgFrameMs:F3} frame_ms_max={maxFrameMs:F3} max_drawn={aggregateStats.MaxDrawnColliders} max_null={aggregateStats.MaxNullColliders} max_inactive={aggregateStats.MaxInactiveColliders} max_culled={aggregateStats.MaxVisibilityCulledColliders} max_segments={aggregateStats.MaxSegments}"
            };
        }

        private void AddTrackedCollider(HitboxType hitboxType, Collider2D collider2D)
        {
            colliders[hitboxType].Add(collider2D);
            if (collider2D != null && collider2D.isActiveAndEnabled)
            {
                activeColliders[hitboxType].Add(collider2D);
            }
        }

        private void RefreshActiveColliderCache(bool force)
        {
            if (!force && Time.realtimeSinceStartup - lastActiveRefreshRealtime < ActiveRefreshIntervalSeconds)
            {
                return;
            }

            foreach (var pair in activeColliders)
            {
                pair.Value.Clear();
            }

            foreach (var pair in colliders)
            {
                pair.Value.RemoveWhere(static collider2D => collider2D == null);

                foreach (Collider2D collider2D in pair.Value)
                {
                    if (collider2D.isActiveAndEnabled)
                    {
                        activeColliders[pair.Key].Add(collider2D);
                    }
                }
            }

            lastActiveRefreshRealtime = Time.realtimeSinceStartup;
        }

        private static bool IsColliderVisible(Camera camera, Collider2D collider2D)
        {
            Bounds bounds = collider2D.bounds;
            if (bounds.size == Vector3.zero)
            {
                return true;
            }

            if (camera.orthographic)
            {
                float halfHeight = camera.orthographicSize;
                float halfWidth = halfHeight * camera.aspect;
                Vector3 cameraPosition = camera.transform.position;
                float left = cameraPosition.x - halfWidth;
                float right = cameraPosition.x + halfWidth;
                float bottom = cameraPosition.y - halfHeight;
                float top = cameraPosition.y + halfHeight;

                return !(bounds.max.x < left || bounds.min.x > right || bounds.max.y < bottom || bounds.min.y > top);
            }

            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
            return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
        }

        private static double StopwatchTicksToMs(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }
    }
}
