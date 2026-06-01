using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Controls;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private bool firstPerson3DEnabled = false;
        private bool firstPerson3DInitialized = false;
        private long firstPerson3DLastRebuildMs = -1;
        private bool firstPerson3DHasMesh = false;
        private int firstPerson3DLastMinX = int.MinValue;
        private int firstPerson3DLastMaxX = int.MinValue;
        private int firstPerson3DLastMinY = int.MinValue;
        private int firstPerson3DLastMaxY = int.MinValue;

        private OrthographicCamera? firstPerson3DCamera;
        private ModelVisual3D? firstPerson3DVisual;
        private Model3DGroup? firstPerson3DRoot;
        private Model3DGroup? firstPerson3DDynamic;
        private GeometryModel3D? firstPerson3DFloorModel;
        private GeometryModel3D? firstPerson3DWallModel;
        private GeometryModel3D? firstPerson3DSlabModel;
        private GeometryModel3D? firstPerson3DHazardModel;
        private Model3DGroup? firstPerson3DSpriteGroup;
        private GeometryModel3D? firstPerson3DPlayerModel;
        private TranslateTransform3D? firstPerson3DPlayerTransform;

        private DiffuseMaterial? firstPerson3DWallMaterial;
        private DiffuseMaterial? firstPerson3DSlabMaterial;
        private MaterialGroup? firstPerson3DHazardMaterial;
        private DiffuseMaterial? firstPerson3DFloorMaterial;
        private MaterialGroup? firstPerson3DPlayerMaterial;
        private readonly Dictionary<int, MaterialGroup> firstPerson3DMarkerMaterials = new Dictionary<int, MaterialGroup>();

        private bool firstPerson3DRotateActive = false;
        private bool firstPerson3DPanActive = false;
        private Point firstPerson3DLastMouse;
        private double firstPerson3DPanX = 0.0;
        private double firstPerson3DPanY = 0.0;
        private double firstPerson3DYawDeg = -135.0;
        private double firstPerson3DPitchDeg = -31.0;
        private const double FirstPersonOrbitDistance = 19.5;

        private const double FirstPersonZoomMin = 45.0;
        private const double FirstPersonZoomMax = 110.0;
        private const double FirstPersonZoomNeutral = (FirstPersonZoomMin + FirstPersonZoomMax) * 0.5;
        private double firstPersonZoomValue = FirstPersonZoomNeutral;

        private const int FirstPerson3DRebuildIntervalMs = 120;
        private const int FirstPersonNearTiles = 14;
        private const int FirstPersonFarTiles = 14;
        private const int FirstPersonUpTiles = 10;
        private const int FirstPersonDownTiles = 10;
        private const int FirstPersonVoxelGrid = 4;

        private void FirstPersonZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            firstPersonZoomValue = Math.Max(FirstPersonZoomMin, Math.Min(FirstPersonZoomMax, e.NewValue));
            if (firstPerson3DCamera != null)
            {
                firstPerson3DCamera.Width = ComputeIsometricWidth(firstPersonZoomValue);
            }

            ApplyZoomForCurrentViewMode();
        }

        private static double ComputeIsometricWidth(double zoomValue)
        {
            double t = (zoomValue - FirstPersonZoomMin) / (FirstPersonZoomMax - FirstPersonZoomMin);
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return 8.0 + (t * 24.0);
        }

        private static double Compute2DZoomScale(double zoomValue)
        {
            // Piecewise mapping centered at slider midpoint:
            // lower values zoom in strongly, higher values zoom out meaningfully.
            if (zoomValue >= FirstPersonZoomNeutral)
            {
                double t = (zoomValue - FirstPersonZoomNeutral) / (FirstPersonZoomMax - FirstPersonZoomNeutral);
                return 1.0 - (t * 0.65); // 1.00 -> 0.35
            }

            {
                double t = (FirstPersonZoomNeutral - zoomValue) / (FirstPersonZoomNeutral - FirstPersonZoomMin);
                return 1.0 + (t * 1.5); // 1.00 -> 2.50
            }
        }

        private void ApplyZoomForCurrentViewMode()
        {
            if (firstPerson3DEnabled) return;

            double zoomScale = Compute2DZoomScale(firstPersonZoomValue);
            sim2DZoomScale = zoomScale;

            int dispScale = Math.Max(1, _simulatorDisplayScale);

            try
            {
                // The canvas lives in world coords (NES pixels).  When zoomed out we expand it so
                // more tiles are visible; LayoutTransform then shrinks it back to fit the viewport.
                // The viewport itself is already sized to NES*dispScale by the constructor, so the
                // combined scale we apply to RenderCanvas must be zoomScale * dispScale so the
                // content exactly fills the GameViewportSurface at every display-scale setting.
                double totalScale = zoomScale * dispScale;
                double newW = NES_W * TILE / zoomScale;   // world-coord canvas width
                double newH = NES_H * TILE / zoomScale;   // world-coord canvas height

                if (RenderCanvas != null)
                {
                    RenderCanvas.Width  = newW;
                    RenderCanvas.Height = newH;
                    RenderCanvas.LayoutTransform = new ScaleTransform(totalScale, totalScale);

                    // Keep background rectangle filling the whole (expanded) canvas.
                    if (bgRectPersistent != null)
                    {
                        bgRectPersistent.Width  = newW;
                        bgRectPersistent.Height = newH;
                    }
                }

                // GameViewportSurface is already sized by the constructor; do not add another
                // LayoutTransform on it or the overlays — that would double-scale them.
                if (GameViewportSurface != null) GameViewportSurface.LayoutTransform = null;
                if (PauseOverlay != null)        PauseOverlay.LayoutTransform        = null;
                if (LevelCompleteOverlay != null) LevelCompleteOverlay.LayoutTransform = null;

                // Force tile-cache rebuild at new coverage size.
                tileLayerCache  = null;
                cachedStartTileX = int.MinValue;
            }
            catch { }
        }

        private void FirstPerson3DCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            firstPerson3DEnabled = FirstPerson3DCheckBox?.IsChecked == true;
            if (firstPerson3DEnabled)
            {
                EnsureFirstPerson3DInitialized();
                if (RenderCanvas != null) RenderCanvas.Visibility = Visibility.Collapsed;
                if (FirstPersonViewport != null) FirstPersonViewport.Visibility = Visibility.Visible;
                if (FirstPersonHintText != null) FirstPersonHintText.Visibility = Visibility.Visible;
                UpdateFirstPerson3DPrototype(force: true);
            }
            else
            {
                firstPerson3DRotateActive = false;
                firstPerson3DPanActive = false;
                if (FirstPersonViewport != null)
                {
                    try { FirstPersonViewport.ReleaseMouseCapture(); } catch { }
                    FirstPersonViewport.Visibility = Visibility.Collapsed;
                }
                if (FirstPersonHintText != null) FirstPersonHintText.Visibility = Visibility.Collapsed;
                if (RenderCanvas != null) RenderCanvas.Visibility = Visibility.Visible;

                // Re-apply slider zoom to 2D renderer when leaving FP mode.
                ApplyZoomForCurrentViewMode();
            }
        }

        private void FirstPersonViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;
            firstPerson3DRotateActive = true;
            firstPerson3DPanActive = false;
            firstPerson3DLastMouse = e.GetPosition(FirstPersonViewport);
            try { FirstPersonViewport.CaptureMouse(); } catch { }
            e.Handled = true;
        }

        private void FirstPersonViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;
            firstPerson3DRotateActive = false;
            if (!firstPerson3DPanActive)
            {
                try { FirstPersonViewport.ReleaseMouseCapture(); } catch { }
            }
            e.Handled = true;
        }

        private void FirstPersonViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;
            firstPerson3DPanActive = true;
            firstPerson3DRotateActive = false;
            firstPerson3DLastMouse = e.GetPosition(FirstPersonViewport);
            try { FirstPersonViewport.CaptureMouse(); } catch { }
            e.Handled = true;
        }

        private void FirstPersonViewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;
            firstPerson3DPanActive = false;
            if (!firstPerson3DRotateActive)
            {
                try { FirstPersonViewport.ReleaseMouseCapture(); } catch { }
            }
            e.Handled = true;
        }

        private void FirstPersonViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!firstPerson3DEnabled || FirstPersonViewport == null) return;

            // Recover gracefully if the initial down event was missed.
            if (!firstPerson3DRotateActive && !firstPerson3DPanActive)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    firstPerson3DRotateActive = true;
                    firstPerson3DPanActive = false;
                    firstPerson3DLastMouse = e.GetPosition(FirstPersonViewport);
                    try { FirstPersonViewport.CaptureMouse(); } catch { }
                }
                else if (e.RightButton == MouseButtonState.Pressed)
                {
                    firstPerson3DPanActive = true;
                    firstPerson3DRotateActive = false;
                    firstPerson3DLastMouse = e.GetPosition(FirstPersonViewport);
                    try { FirstPersonViewport.CaptureMouse(); } catch { }
                }
                else
                {
                    return;
                }
            }

            bool rotateHeld = e.LeftButton == MouseButtonState.Pressed;
            bool panHeld = e.RightButton == MouseButtonState.Pressed;

            if (firstPerson3DRotateActive && !rotateHeld)
            {
                firstPerson3DRotateActive = false;
            }
            if (firstPerson3DPanActive && !panHeld)
            {
                firstPerson3DPanActive = false;
            }
            if (!firstPerson3DRotateActive && !firstPerson3DPanActive)
            {
                try { FirstPersonViewport.ReleaseMouseCapture(); } catch { }
                return;
            }

            Point now = e.GetPosition(FirstPersonViewport);
            double dx = now.X - firstPerson3DLastMouse.X;
            double dy = now.Y - firstPerson3DLastMouse.Y;
            firstPerson3DLastMouse = now;

            if (firstPerson3DRotateActive)
            {
                // Left-drag rotates camera around the target.
                firstPerson3DYawDeg += dx * 0.32;
                firstPerson3DPitchDeg += dy * 0.22;
                if (firstPerson3DPitchDeg > -8.0) firstPerson3DPitchDeg = -8.0;
                if (firstPerson3DPitchDeg < -80.0) firstPerson3DPitchDeg = -80.0;
            }
            else if (firstPerson3DPanActive)
            {
                // Right-drag pans target in world-space.
                double worldPerPixel = ComputeIsometricWidth(firstPersonZoomValue) / 256.0;
                firstPerson3DPanX -= dx * worldPerPixel * 0.95;
                firstPerson3DPanY += dy * worldPerPixel * 0.95;
            }

            UpdateFirstPerson3DPrototype(force: false);
            e.Handled = true;
        }

        private void EnsureFirstPerson3DInitialized()
        {
            if (firstPerson3DInitialized) return;
            if (FirstPersonViewport == null) return;

            firstPerson3DWallMaterial = CreateMaterial(Color.FromRgb(88, 122, 178));
            firstPerson3DSlabMaterial = CreateMaterial(Color.FromRgb(56, 87, 137));
            firstPerson3DFloorMaterial = CreateMaterial(Color.FromRgb(28, 31, 40));

            var hazardDiffuse = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(230, 95, 55)));
            var hazardEmissive = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(175, 255, 120, 80)));
            if (hazardDiffuse.CanFreeze) hazardDiffuse.Freeze();
            if (hazardEmissive.CanFreeze) hazardEmissive.Freeze();
            firstPerson3DHazardMaterial = new MaterialGroup();
            firstPerson3DHazardMaterial.Children.Add(hazardDiffuse);
            firstPerson3DHazardMaterial.Children.Add(hazardEmissive);

            var playerDiffuse = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(130, 255, 120)));
            var playerEmissive = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(170, 110, 255, 120)));
            if (playerDiffuse.CanFreeze) playerDiffuse.Freeze();
            if (playerEmissive.CanFreeze) playerEmissive.Freeze();
            firstPerson3DPlayerMaterial = new MaterialGroup();
            firstPerson3DPlayerMaterial.Children.Add(playerDiffuse);
            firstPerson3DPlayerMaterial.Children.Add(playerEmissive);

            firstPerson3DCamera = new OrthographicCamera
            {
                Width = ComputeIsometricWidth(firstPersonZoomValue),
                Position = new Point3D(0, 0, -0.25),
                LookDirection = new Vector3D(1.7, 0.0, 0.0),
                UpDirection = new Vector3D(0, 1, 0),
                NearPlaneDistance = 0.05,
                FarPlaneDistance = 120
            };

            try
            {
                if (FirstPersonZoomSlider != null)
                {
                    firstPersonZoomValue = Math.Max(45.0, Math.Min(110.0, FirstPersonZoomSlider.Value));
                    firstPerson3DCamera.Width = ComputeIsometricWidth(firstPersonZoomValue);
                }
            }
            catch { }

            firstPerson3DRoot = new Model3DGroup();
            firstPerson3DDynamic = new Model3DGroup();
            firstPerson3DRoot.Children.Add(new AmbientLight(Color.FromRgb(96, 96, 110)));
            firstPerson3DRoot.Children.Add(new DirectionalLight(Color.FromRgb(230, 230, 230), new Vector3D(-0.45, -0.75, -0.25)));
            firstPerson3DRoot.Children.Add(firstPerson3DDynamic);

            firstPerson3DVisual = new ModelVisual3D { Content = firstPerson3DRoot };

            firstPerson3DFloorModel = new GeometryModel3D();
            firstPerson3DWallModel = new GeometryModel3D();
            firstPerson3DSlabModel = new GeometryModel3D();
            firstPerson3DHazardModel = new GeometryModel3D();
            firstPerson3DSpriteGroup = new Model3DGroup();
            firstPerson3DPlayerModel = new GeometryModel3D();
            firstPerson3DPlayerTransform = new TranslateTransform3D();

            firstPerson3DFloorModel.Material = firstPerson3DFloorMaterial;
            firstPerson3DFloorModel.BackMaterial = firstPerson3DFloorMaterial;
            firstPerson3DWallModel.Material = firstPerson3DWallMaterial;
            firstPerson3DWallModel.BackMaterial = firstPerson3DWallMaterial;
            firstPerson3DSlabModel.Material = firstPerson3DSlabMaterial;
            firstPerson3DSlabModel.BackMaterial = firstPerson3DSlabMaterial;
            firstPerson3DHazardModel.Material = firstPerson3DHazardMaterial;
            firstPerson3DHazardModel.BackMaterial = firstPerson3DHazardMaterial;

            firstPerson3DPlayerModel.Material = firstPerson3DPlayerMaterial;
            firstPerson3DPlayerModel.BackMaterial = firstPerson3DPlayerMaterial;
            firstPerson3DPlayerModel.Transform = firstPerson3DPlayerTransform;

            var playerMesh = new MeshGeometry3D();
            AddBox(playerMesh, -0.23, 0.00, -0.23, 0.23, 0.92, 0.23);
            firstPerson3DPlayerModel.Geometry = playerMesh;

            firstPerson3DDynamic.Children.Add(firstPerson3DFloorModel);
            firstPerson3DDynamic.Children.Add(firstPerson3DWallModel);
            firstPerson3DDynamic.Children.Add(firstPerson3DSlabModel);
            firstPerson3DDynamic.Children.Add(firstPerson3DHazardModel);
            firstPerson3DDynamic.Children.Add(firstPerson3DSpriteGroup);
            firstPerson3DDynamic.Children.Add(firstPerson3DPlayerModel);

            FirstPersonViewport.Camera = firstPerson3DCamera;
            FirstPersonViewport.Children.Clear();
            FirstPersonViewport.Children.Add(firstPerson3DVisual);

            firstPerson3DInitialized = true;
        }

        private void UpdateFirstPerson3DPrototype(bool force = false)
        {
            if (!firstPerson3DEnabled) return;
            if (windowClosed) return;
            EnsureFirstPerson3DInitialized();
            if (!firstPerson3DInitialized || firstPerson3DCamera == null) return;

            int playerXpx = playerX_fixed >> 8;
            int playerYpx = playerY_fixed >> 8;
            int playerTileX = playerXpx / TILE;
            int playerTileY = Math.Max(0, playerYpx / TILE);

            double playerWorldX = (playerXpx + (TILE / 2.0)) / TILE;
            double playerFeetWorldY = -((playerYpx + playerVisualHeight) / (double)TILE);

            // Isometric top-right view with drag panning offsets.
            double targetX = playerWorldX + firstPerson3DPanX;
            double targetY = playerFeetWorldY + 0.7 + firstPerson3DPanY;
            double targetZ = 0.0;
            double yawRad = firstPerson3DYawDeg * Math.PI / 180.0;
            double pitchRad = firstPerson3DPitchDeg * Math.PI / 180.0;

            double lookX = Math.Cos(pitchRad) * Math.Cos(yawRad);
            double lookY = Math.Sin(pitchRad);
            double lookZ = Math.Cos(pitchRad) * Math.Sin(yawRad);
            var lookDir = new Vector3D(lookX, lookY, lookZ);
            lookDir.Normalize();

            firstPerson3DCamera.Position = new Point3D(
                targetX - (lookDir.X * FirstPersonOrbitDistance),
                targetY - (lookDir.Y * FirstPersonOrbitDistance),
                targetZ - (lookDir.Z * FirstPersonOrbitDistance));
            firstPerson3DCamera.LookDirection = new Vector3D(lookDir.X * FirstPersonOrbitDistance, lookDir.Y * FirstPersonOrbitDistance, lookDir.Z * FirstPersonOrbitDistance);
            firstPerson3DCamera.UpDirection = new Vector3D(0, 1, 0);
            firstPerson3DCamera.Width = ComputeIsometricWidth(firstPersonZoomValue);

            if (firstPerson3DPlayerTransform != null)
            {
                firstPerson3DPlayerTransform.OffsetX = playerWorldX;
                firstPerson3DPlayerTransform.OffsetY = playerFeetWorldY;
                firstPerson3DPlayerTransform.OffsetZ = 0.0;
            }

            if (!ShouldRebuildFirstPersonGeometry(playerTileX, playerTileY, force))
            {
                return;
            }

            BuildFirstPersonGeometry(playerTileX, playerTileY);
        }

        private bool ShouldRebuildFirstPersonGeometry(int playerTileX, int playerTileY, bool force)
        {
            if (force || !firstPerson3DHasMesh) return true;

            int minX = Math.Max(0, playerTileX - FirstPersonNearTiles);
            int maxX = Math.Min(mapWidth - 1, playerTileX + FirstPersonFarTiles);
            int minY = Math.Max(0, playerTileY - FirstPersonUpTiles);
            int maxY = Math.Min(mapHeight - 1, playerTileY + FirstPersonDownTiles);

            if (minX != firstPerson3DLastMinX || maxX != firstPerson3DLastMaxX || minY != firstPerson3DLastMinY || maxY != firstPerson3DLastMaxY)
            {
                long now = Environment.TickCount64;
                if (firstPerson3DLastRebuildMs < 0 || (now - firstPerson3DLastRebuildMs) >= FirstPerson3DRebuildIntervalMs)
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildFirstPersonGeometry(int playerTileX, int playerTileY)
        {
            if (firstPerson3DWallModel == null || firstPerson3DSlabModel == null || firstPerson3DHazardModel == null || firstPerson3DFloorModel == null || firstPerson3DSpriteGroup == null) return;

            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

            int minX = Math.Max(0, playerTileX - FirstPersonNearTiles);
            int maxX = Math.Min(mapWidth - 1, playerTileX + FirstPersonFarTiles);
            int minY = Math.Max(0, playerTileY - FirstPersonUpTiles);
            int maxY = Math.Min(mapHeight - 1, playerTileY + FirstPersonDownTiles);

            var wallMesh = new MeshGeometry3D();
            var slabMesh = new MeshGeometry3D();
            var hazardMesh = new MeshGeometry3D();
            var floorMesh = new MeshGeometry3D();
            var markerMeshes = new Dictionary<int, MeshGeometry3D>();

            for (int tyWorld = minY; tyWorld <= maxY; tyWorld++)
            {
                int tyArray = tyWorld + groundRowsToReserve;
                if (tyArray < 0 || tyArray >= mapHeight) continue;

                for (int tx = minX; tx <= maxX; tx++)
                {
                    int idx = tyArray * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;

                    int tid = tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));
                    if (col == MetatileCollision.COL_NONE || SharedPhysics.IsSlopeTile(col)) continue;

                    double x0 = tx;
                    double x1 = tx + 1.0;
                    double yTop = -tyWorld;
                    double yBottom = yTop - 1.0;

                    AddCollisionTileGeometry(col, x0, x1, yBottom, yTop, wallMesh, slabMesh, hazardMesh);
                }
            }

            // Sprite markers: first pass for orb and portal families.
            foreach (int sidx in nonEmptySpriteIndices)
            {
                if (sidx < 0 || sidx >= sprites.Length) continue;
                int sid = sprites[sidx] & 0xFF;

                bool isOrb = SharedPhysics.IsOrbSprite(sid) || SharedPhysics.IsSpiderOrb(sid);
                bool isPortal = IsPortalSpriteSid(sid);
                if (!isOrb && !isPortal) continue;

                int sx = sidx % mapWidth;
                int syArray = sidx / mapWidth;
                int syWorld = syArray - groundRowsToReserve;

                if (sx < minX - 1 || sx > maxX + 1 || syWorld < minY - 1 || syWorld > maxY + 1) continue;

                int materialKey = GetSpriteMarkerMaterialKey(sid, isPortal);
                if (!markerMeshes.TryGetValue(materialKey, out MeshGeometry3D? mm))
                {
                    mm = new MeshGeometry3D();
                    markerMeshes[materialKey] = mm;
                }

                double cx = sx + 0.5;
                double cy = -(syWorld + 0.5);

                if (isPortal)
                {
                    AddPortalMarker(mm, cx, cy, 0.0);
                }
                else
                {
                    double orbScale = 1.0;
                    if (SharedPhysics.IsYellowOrbBigger(sid)) orbScale = 1.2;
                    else if (SharedPhysics.IsYellowOrbSmaller(sid)) orbScale = 0.85;
                    AddOrbMarker(mm, cx, cy, 0.0, orbScale);
                }
            }

            // Floor sits at the bottom of the rendered tile window and uses a real slab
            // so it stays visually attached when the camera rotates and exposes depth.
            double floorY = -(maxY + 1.0) + 3.0;
            AddBox(floorMesh,
                minX - 2.0,
                floorY - 1.25,
                -8.0,
                maxX + 3.0,
                floorY,
                8.0);

            firstPerson3DFloorModel.Geometry = floorMesh;
            firstPerson3DWallModel.Geometry = wallMesh;
            firstPerson3DSlabModel.Geometry = slabMesh;
            firstPerson3DHazardModel.Geometry = hazardMesh;

            firstPerson3DSpriteGroup.Children.Clear();
            foreach (var kvp in markerMeshes)
            {
                if (kvp.Value.Positions.Count == 0) continue;
                var markerModel = new GeometryModel3D();
                markerModel.Geometry = kvp.Value;
                var markerMaterial = GetSpriteMarkerMaterial(kvp.Key);
                markerModel.Material = markerMaterial;
                markerModel.BackMaterial = markerMaterial;
                firstPerson3DSpriteGroup.Children.Add(markerModel);
            }

            firstPerson3DHasMesh = true;
            firstPerson3DLastRebuildMs = Environment.TickCount64;
            firstPerson3DLastMinX = minX;
            firstPerson3DLastMaxX = maxX;
            firstPerson3DLastMinY = minY;
            firstPerson3DLastMaxY = maxY;
        }

        private static void AddCollisionTileGeometry(
            MetatileCollision col,
            double x0,
            double x1,
            double yBottom,
            double yTop,
            MeshGeometry3D wallMesh,
            MeshGeometry3D slabMesh,
            MeshGeometry3D hazardMesh)
        {
            const double zNear = -0.55;
            const double zFar = 0.55;

            if (IsSpikeStyleCollision(col))
            {
                AddSpikeGeometry(col, x0, x1, yBottom, yTop, slabMesh, hazardMesh, zNear, zFar);
                return;
            }

            int solidCells = 0;
            int deathCells = 0;
            int cells = FirstPersonVoxelGrid * FirstPersonVoxelGrid;

            bool[] solidMask = new bool[cells];
            bool[] deathMask = new bool[cells];

            for (int cy = 0; cy < FirstPersonVoxelGrid; cy++)
            {
                for (int cx = 0; cx < FirstPersonVoxelGrid; cx++)
                {
                    int localX = cx * (16 / FirstPersonVoxelGrid) + (8 / FirstPersonVoxelGrid);
                    int localY = cy * (16 / FirstPersonVoxelGrid) + (8 / FirstPersonVoxelGrid);
                    bool solid = TileOccupiesPixel(col, localX, localY);
                    bool death = MetatileCollisionTable.TileKillsAtPixel(col, localX, localY);

                    int i = cy * FirstPersonVoxelGrid + cx;
                    solidMask[i] = solid;
                    deathMask[i] = death;
                    if (solid) solidCells++;
                    if (death) deathCells++;
                }
            }

            if (solidCells == cells && deathCells == 0)
            {
                AddBox(wallMesh, x0, yBottom, zNear, x1, yTop, zFar);
                return;
            }

            double step = 1.0 / FirstPersonVoxelGrid;

            // Emit only exposed surfaces so adjacent voxels do not create coplanar
            // internal faces that flicker due to z-fighting.
            EmitVoxelSurface(slabMesh, solidMask, x0, yTop, zNear, zFar, step, 0.0);
            EmitVoxelSurface(hazardMesh, deathMask, x0, yTop, zNear + 0.18, zFar - 0.18, step, step * 0.08);

            // If this tile is shape-empty in the sampled mask but still classifies as
            // death (rare mixed shapes), keep a tiny fallback marker.
            if (solidCells == 0 && deathCells > 0)
            {
                AddBox(hazardMesh, x0 + 0.36, yBottom + 0.36, -0.08, x1 - 0.36, yTop - 0.36, 0.08);
            }
        }

        private static void EmitVoxelSurface(
            MeshGeometry3D mesh,
            bool[] mask,
            double tileX0,
            double tileYTop,
            double zNear,
            double zFar,
            double step,
            double inset)
        {
            int grid = FirstPersonVoxelGrid;
            for (int cy = 0; cy < grid; cy++)
            {
                for (int cx = 0; cx < grid; cx++)
                {
                    int i = cy * grid + cx;
                    if (!mask[i]) continue;

                    double vx0 = tileX0 + (cx * step) + inset;
                    double vx1 = tileX0 + ((cx + 1) * step) - inset;
                    double vyTop = tileYTop - (cy * step) - inset;
                    double vyBottom = tileYTop - ((cy + 1) * step) + inset;
                    if (vx1 <= vx0 || vyTop <= vyBottom) continue;

                    bool leftExposed = (cx == 0) || !mask[(cy * grid) + (cx - 1)];
                    bool rightExposed = (cx == grid - 1) || !mask[(cy * grid) + (cx + 1)];
                    bool topExposed = (cy == 0) || !mask[((cy - 1) * grid) + cx];
                    bool bottomExposed = (cy == grid - 1) || !mask[((cy + 1) * grid) + cx];

                    // Always add front/back so visible silhouette remains solid.
                    AddQuad(mesh, new Point3D(vx0, vyBottom, zNear), new Point3D(vx1, vyBottom, zNear), new Point3D(vx1, vyTop, zNear), new Point3D(vx0, vyTop, zNear));
                    AddQuad(mesh, new Point3D(vx1, vyBottom, zFar), new Point3D(vx0, vyBottom, zFar), new Point3D(vx0, vyTop, zFar), new Point3D(vx1, vyTop, zFar));

                    if (leftExposed)
                    {
                        AddQuad(mesh, new Point3D(vx0, vyBottom, zFar), new Point3D(vx0, vyBottom, zNear), new Point3D(vx0, vyTop, zNear), new Point3D(vx0, vyTop, zFar));
                    }
                    if (rightExposed)
                    {
                        AddQuad(mesh, new Point3D(vx1, vyBottom, zNear), new Point3D(vx1, vyBottom, zFar), new Point3D(vx1, vyTop, zFar), new Point3D(vx1, vyTop, zNear));
                    }
                    if (topExposed)
                    {
                        AddQuad(mesh, new Point3D(vx0, vyTop, zNear), new Point3D(vx1, vyTop, zNear), new Point3D(vx1, vyTop, zFar), new Point3D(vx0, vyTop, zFar));
                    }
                    if (bottomExposed)
                    {
                        AddQuad(mesh, new Point3D(vx0, vyBottom, zFar), new Point3D(vx1, vyBottom, zFar), new Point3D(vx1, vyBottom, zNear), new Point3D(vx0, vyBottom, zNear));
                    }
                }
            }
        }

        private static bool IsSpikeStyleCollision(MetatileCollision col)
        {
            switch (col)
            {
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                case MetatileCollision.COL_UP_BOTH_SPIKES:
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsPortalSpriteSid(int sid)
        {
            if (SharedPhysics.IsSpeedPortal(sid) || SharedPhysics.IsGameModePortal(sid) || SharedPhysics.IsGravityPortal(sid) || SharedPhysics.IsMiniGrowthPortal(sid))
            {
                return true;
            }

            // Portal-like special classes used by simulator logic.
            if (sid == 0x22 || sid == 0x23 || sid == 0x8E || sid == 0x9E)
            {
                return true;
            }

            // Gravity modifier portals (0x5F-0x63).
            if (sid >= 0x5F && sid <= 0x63)
            {
                return true;
            }

            return false;
        }

        private static int GetSpriteMarkerMaterialKey(int sid, bool isPortal)
        {
            if (!isPortal)
            {
                if (SharedPhysics.IsYellowOrb(sid) || SharedPhysics.IsYellowOrbBigger(sid) || SharedPhysics.IsYellowOrbSmaller(sid)) return 100;
                if (SharedPhysics.IsPinkOrb(sid)) return 101;
                if (SharedPhysics.IsRedOrb(sid)) return 102;
                if (SharedPhysics.IsBlueOrb(sid)) return 103;
                if (SharedPhysics.IsGreenOrb(sid)) return 104;
                if (SharedPhysics.IsBlackOrb(sid)) return 105;
                if (SharedPhysics.IsWhiteOrb(sid)) return 106;
                if (SharedPhysics.IsSpiderOrb(sid)) return 107;
                return 100;
            }

            if (SharedPhysics.IsGravityPortal(sid))
            {
                return SharedPhysics.IsReverseGravity(sid) ? 201 : 200;
            }
            if (SharedPhysics.IsMiniGrowthPortal(sid))
            {
                return sid == 0x18 ? 202 : 203;
            }
            if (SharedPhysics.IsSpeedPortal(sid)) return 204;
            if (SharedPhysics.IsGameModePortal(sid)) return 205;
            if (sid == 0x22 || sid == 0x23) return 206;
            if (sid == 0x8E || sid == 0x9E) return 207;
            if (sid >= 0x5F && sid <= 0x63) return 208;
            return 205;
        }

        private MaterialGroup GetSpriteMarkerMaterial(int key)
        {
            if (firstPerson3DMarkerMaterials.TryGetValue(key, out MaterialGroup? existing))
            {
                return existing;
            }

            Color diffuse;
            Color emissive;
            switch (key)
            {
                case 100: diffuse = Color.FromRgb(245, 222, 68); emissive = Color.FromArgb(170, 255, 244, 120); break; // yellow orb
                case 101: diffuse = Color.FromRgb(255, 120, 220); emissive = Color.FromArgb(170, 255, 180, 235); break; // pink orb
                case 102: diffuse = Color.FromRgb(255, 92, 78); emissive = Color.FromArgb(165, 255, 150, 130); break;  // red orb
                case 103: diffuse = Color.FromRgb(86, 172, 255); emissive = Color.FromArgb(170, 138, 214, 255); break; // blue orb
                case 104: diffuse = Color.FromRgb(90, 238, 112); emissive = Color.FromArgb(170, 140, 255, 168); break; // green orb
                case 105: diffuse = Color.FromRgb(30, 30, 36); emissive = Color.FromArgb(120, 110, 110, 140); break;   // black orb
                case 106: diffuse = Color.FromRgb(245, 245, 245); emissive = Color.FromArgb(170, 255, 255, 255); break; // white orb
                case 107: diffuse = Color.FromRgb(170, 118, 255); emissive = Color.FromArgb(170, 205, 175, 255); break; // spider orb
                case 200: diffuse = Color.FromRgb(112, 228, 255); emissive = Color.FromArgb(170, 160, 245, 255); break; // gravity portal normal
                case 201: diffuse = Color.FromRgb(255, 118, 232); emissive = Color.FromArgb(170, 255, 155, 245); break; // gravity portal reverse
                case 202: diffuse = Color.FromRgb(114, 255, 148); emissive = Color.FromArgb(170, 165, 255, 190); break; // mini
                case 203: diffuse = Color.FromRgb(255, 176, 96); emissive = Color.FromArgb(170, 255, 214, 156); break;  // growth
                case 204: diffuse = Color.FromRgb(255, 215, 78); emissive = Color.FromArgb(170, 255, 235, 146); break;  // speed portal
                case 205: diffuse = Color.FromRgb(152, 168, 255); emissive = Color.FromArgb(170, 190, 210, 255); break; // gamemode portal
                case 206: diffuse = Color.FromRgb(248, 248, 248); emissive = Color.FromArgb(170, 255, 255, 255); break; // dual/single
                case 207: diffuse = Color.FromRgb(110, 255, 244); emissive = Color.FromArgb(170, 150, 255, 250); break; // wrap
                case 208: diffuse = Color.FromRgb(255, 137, 96); emissive = Color.FromArgb(170, 255, 180, 150); break;  // gravity modifier
                default: diffuse = Color.FromRgb(190, 190, 220); emissive = Color.FromArgb(140, 210, 210, 245); break;
            }

            var d = new DiffuseMaterial(new SolidColorBrush(diffuse));
            var e = new EmissiveMaterial(new SolidColorBrush(emissive));
            if (d.CanFreeze) d.Freeze();
            if (e.CanFreeze) e.Freeze();

            var group = new MaterialGroup();
            group.Children.Add(d);
            group.Children.Add(e);
            firstPerson3DMarkerMaterials[key] = group;
            return group;
        }

        private static void AddOrbMarker(MeshGeometry3D mesh, double cx, double cy, double cz, double scale)
        {
            double outerR = 0.26 * scale;
            double innerR = 0.14 * scale;
            double depth = 0.075;
            AddRadialRing(mesh, cx, cy, cz, outerR, innerR, depth, 14);
            AddBox(mesh, cx - (innerR * 0.48), cy - (innerR * 0.48), cz - (depth * 0.55), cx + (innerR * 0.48), cy + (innerR * 0.48), cz + (depth * 0.55));
        }

        private static void AddPortalMarker(MeshGeometry3D mesh, double cx, double cy, double cz)
        {
            double outerR = 0.42;
            double innerR = 0.30;
            double depth = 0.085;
            double verticalStretch = 1.55;
            AddRadialRing(mesh, cx, cy, cz, outerR, innerR, depth, 18, verticalStretch);
            AddBox(mesh, cx - 0.06, cy - 0.22, cz - (depth * 0.45), cx + 0.06, cy + 0.22, cz + (depth * 0.45));
        }

        private static void AddRadialRing(
            MeshGeometry3D mesh,
            double cx,
            double cy,
            double cz,
            double outerR,
            double innerR,
            double halfDepth,
            int segments,
            double yScale = 1.0)
        {
            if (segments < 3) segments = 3;
            for (int s = 0; s < segments; s++)
            {
                double a0 = (Math.PI * 2.0 * s) / segments;
                double a1 = (Math.PI * 2.0 * (s + 1)) / segments;

                double c0 = Math.Cos(a0);
                double n0 = Math.Sin(a0);
                double c1 = Math.Cos(a1);
                double n1 = Math.Sin(a1);

                Point3D o0f = new Point3D(cx + (outerR * c0), cy + (outerR * n0 * yScale), cz - halfDepth);
                Point3D o1f = new Point3D(cx + (outerR * c1), cy + (outerR * n1 * yScale), cz - halfDepth);
                Point3D i0f = new Point3D(cx + (innerR * c0), cy + (innerR * n0 * yScale), cz - halfDepth);
                Point3D i1f = new Point3D(cx + (innerR * c1), cy + (innerR * n1 * yScale), cz - halfDepth);

                Point3D o0b = new Point3D(o0f.X, o0f.Y, cz + halfDepth);
                Point3D o1b = new Point3D(o1f.X, o1f.Y, cz + halfDepth);
                Point3D i0b = new Point3D(i0f.X, i0f.Y, cz + halfDepth);
                Point3D i1b = new Point3D(i1f.X, i1f.Y, cz + halfDepth);

                // Front and back annulus slices.
                AddQuad(mesh, o0f, o1f, i1f, i0f);
                AddQuad(mesh, o1b, o0b, i0b, i1b);

                // Outer and inner side walls.
                AddQuad(mesh, o0f, o0b, o1b, o1f);
                AddQuad(mesh, i1f, i1b, i0b, i0f);
            }
        }

        private static void AddSpikeGeometry(
            MetatileCollision col,
            double x0,
            double x1,
            double yBottom,
            double yTop,
            MeshGeometry3D slabMesh,
            MeshGeometry3D hazardMesh,
            double zNear,
            double zFar)
        {
            double xMid = (x0 + x1) * 0.5;
            double yMid = (yTop + yBottom) * 0.5;

            void AddTopSlab() => AddBox(slabMesh, x0, yMid, zNear, x1, yTop, zFar);
            void AddBottomSlab() => AddBox(slabMesh, x0, yBottom, zNear, x1, yMid, zFar);
            void AddBottomLeftQuarter() => AddBox(slabMesh, x0, yBottom, zNear, xMid, yMid, zFar);
            void AddBottomRightQuarter() => AddBox(slabMesh, xMid, yBottom, zNear, x1, yMid, zFar);

            void AddCenterSpikeDown() => AddSpikePrism(hazardMesh, x0 + 0.18, x1 - 0.18, yMid, xMid, yBottom + 0.02, zNear, zFar);
            void AddCenterSpikeUp() => AddSpikePrism(hazardMesh, x0 + 0.18, x1 - 0.18, yMid, xMid, yTop - 0.02, zNear, zFar);
            void AddLeftSpikeUp() => AddSpikePrism(hazardMesh, x0 + 0.04, x0 + 0.52, yMid, x0 + 0.28, yTop - 0.03, zNear, zFar);
            void AddRightSpikeUp() => AddSpikePrism(hazardMesh, x1 - 0.52, x1 - 0.04, yMid, x1 - 0.28, yTop - 0.03, zNear, zFar);
            void AddLeftSpikeDown() => AddSpikePrism(hazardMesh, x0 + 0.04, x0 + 0.52, yTop, x0 + 0.28, yMid + 0.02, zNear, zFar);
            void AddRightSpikeDown() => AddSpikePrism(hazardMesh, x1 - 0.52, x1 - 0.04, yTop, x1 - 0.28, yMid + 0.02, zNear, zFar);
            void AddLeftSpikeBottomUp() => AddSpikePrism(hazardMesh, x0 + 0.04, x0 + 0.52, yBottom, x0 + 0.28, yMid - 0.02, zNear, zFar);
            void AddRightSpikeBottomUp() => AddSpikePrism(hazardMesh, x1 - 0.52, x1 - 0.04, yBottom, x1 - 0.28, yMid - 0.02, zNear, zFar);

            switch (col)
            {
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    AddTopSlab();
                    AddCenterSpikeDown();
                    break;
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                    AddBottomSlab();
                    AddCenterSpikeUp();
                    break;
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    AddBottomLeftQuarter();
                    AddLeftSpikeUp();
                    break;
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    AddBottomRightQuarter();
                    AddRightSpikeUp();
                    break;
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    AddBottomSlab();
                    AddLeftSpikeUp();
                    break;
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    AddBottomSlab();
                    AddRightSpikeUp();
                    break;
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    AddBottomSlab();
                    AddLeftSpikeUp();
                    AddRightSpikeUp();
                    break;
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                    AddLeftSpikeDown();
                    break;
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                    AddRightSpikeDown();
                    break;
                case MetatileCollision.COL_UP_BOTH_SPIKES:
                    AddLeftSpikeDown();
                    AddRightSpikeDown();
                    break;
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                    AddLeftSpikeBottomUp();
                    break;
                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                    AddRightSpikeBottomUp();
                    break;
                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                    AddLeftSpikeBottomUp();
                    AddRightSpikeBottomUp();
                    break;
            }
        }

        private static DiffuseMaterial CreateMaterial(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            var material = new DiffuseMaterial(brush);
            if (material.CanFreeze) material.Freeze();
            return material;
        }

        private static void AddBox(MeshGeometry3D mesh, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            AddQuad(mesh, new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0));
            AddQuad(mesh, new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Point3D(x1, y1, z1));
            AddQuad(mesh, new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1));
            AddQuad(mesh, new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x1, y1, z0));
            AddQuad(mesh, new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1));
            AddQuad(mesh, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0));
        }

        private static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.Positions.Add(d);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 3);
        }

        private static void AddTriangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(a);
            mesh.Positions.Add(b);
            mesh.Positions.Add(c);
            mesh.TriangleIndices.Add(i + 0);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
        }

        private static void AddSpikePrism(MeshGeometry3D mesh, double xBaseLeft, double xBaseRight, double yBase, double xTip, double yTip, double zNear, double zFar)
        {
            var aNear = new Point3D(xBaseLeft, yBase, zNear);
            var bNear = new Point3D(xBaseRight, yBase, zNear);
            var cNear = new Point3D(xTip, yTip, zNear);

            var aFar = new Point3D(xBaseLeft, yBase, zFar);
            var bFar = new Point3D(xBaseRight, yBase, zFar);
            var cFar = new Point3D(xTip, yTip, zFar);

            AddTriangle(mesh, aNear, bNear, cNear);
            AddTriangle(mesh, bFar, aFar, cFar);
            AddQuad(mesh, aNear, bNear, bFar, aFar);
            AddQuad(mesh, bNear, cNear, cFar, bFar);
            AddQuad(mesh, cNear, aNear, aFar, cFar);
        }
    }
}
